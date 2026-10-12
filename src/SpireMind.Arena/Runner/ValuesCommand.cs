using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena values</c>: what one card more, one card less or one upgrade is worth in a fight,
/// measured instead of guessed. It takes fights from the arena's own generator (a deck, HP,
/// potions, an encounter and a seed each), plays every one as it is, and plays it again with
/// one change to the deck and everything else the same, with the scripted players. The
/// difference between the two fights, averaged over many fights, is the value of the change.
/// <para>
/// The changes: one copy of each card of the Ironclad's pool added, plain and upgraded; one
/// Strike or one Defend removed; one card of the deck upgraded, for every kind of card in it.
/// Every change is played on the same fights (a block design), so two cards can be compared
/// on equal terms. An added card goes to the end of the deck list, so "plus card X" and "plus
/// a Strike" start from the same shuffle with X where the Strike would be.
/// </para>
/// <para>
/// Nothing of the game is changed and nothing outside the output folder is written. The logs
/// (<c>fights.N.csv</c>, one per worker process) are appended fight by fight, so the same
/// command carries on after a stop.
/// </para>
/// </summary>
public static class ValuesCommand
{
    public const string Usage =
        "  scripts/arena.sh values --out DIR [--seed S=1] [--contexts N=300] [--full K=150] [--min-full 100]\n" +
        "                       [--workers W=6] [--budget-minutes M=45] [--stop-after-minutes X] [--no-greedy-all]\n" +
        "                       [--pilot-jobs P=200] [--cards A,B+,...] [--aa K=0] [--summarize]\n" +
        "                       [--player NAME=Lookahead] [--tiers normal,elite,boss] [--only-adds]\n" +
        "  scripts/arena.sh values --trace CONTEXT:CHANGE:PLAYER\n" +
        "      Measure what a change to the deck is worth: N generated fights of each of the tiers normal,\n" +
        "      elite and boss are played as they are and with one change (a card added, plain or upgraded;\n" +
        "      a Strike or Defend removed; a card upgraded). The first K fights of each tier get every\n" +
        "      addition; K is cut (never below --min-full) so that the run fits in M minutes. Lookahead and\n" +
        "      Greedy both play, in W worker processes (at most 6). The same command with the same DIR\n" +
        "      carries on; --summarize only rewrites the tables from DIR's logs. --cards plays only the additions named\n" +
        "      (class names, a + for the upgraded card). --aa K also plays every fight K times with the same deck in\n" +
        "      another order: a change that changes nothing but the draw, to measure the noise. --trace plays one fight\n" +
        "      of the measurement again and prints every decision (elite-123:add:Bash:Lookahead).\n" +
        "      --player names the searching player in Lookahead's place (Rollout, Rollout:fast, Rollout:24/6/60: see\n" +
        "      Policies.ByName); its rows carry its name. --tiers plays only the tiers named. --only-adds plays, on the\n" +
        "      first K fights of each tier, one more Strike and each addition of --cards and nothing else (no fight as\n" +
        "      it is, no removals, no upgrades, no Greedy, no pilot), and prints each card against the Strike.";

    /// <summary>The searching player's name: Lookahead unless --player names another (it is a field, not a constant, for that).</summary>
    private static string LookaheadName = "Lookahead";
    private const string GreedyName = "Greedy";
    private const string Strike = Scenarios.StrikeCard, Defend = Scenarios.DefendCard;
    private const string BaseId = "base", AddStrikeId = "add:" + Strike, AddDefendId = "add:" + Defend;
    private const int MostWorkers = 6;
    private const int MostDecisions = 6000;
    private const int MinEnemyPairs = 40;

    /// <summary>The share of the time budget the estimate may fill: the rest is for starting the workers, the tables, and a pilot that guessed low.</summary>
    private const double BudgetShare = 0.92;
    private static string[] TierOrder = [Scenarios.NormalTier, Scenarios.EliteTier, Scenarios.BossTier];

    /// <summary>--only-adds: one more Strike and the additions named, on the fights that get additions, and nothing else.</summary>
    private static bool _onlyAdds;

    /// <summary>Incoming damage after the Block the player already has, at the first decision of a turn: none is 0,
    /// small is under this, large is this or more.</summary>
    internal const int LargeDanger = 10;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    /// <summary>The cards whose value a one-turn player was expected to miss most, named before the run:
    /// Powers that pay over many turns and cost a turn's energy to set up.</summary>
    internal static readonly string[] ExpectedUndervalued =
    [
        "DemonForm", "Barricade", "Corruption", "DarkEmbrace", "FeelNoPain", "Juggernaut", "Rupture", "Inferno",
        "Pyre", "Unmovable", "Cruelty", "CrimsonMantle", "Aggression", "Hellraiser", "Juggling",
    ];

    // =====================================================================================
    // Options
    // =====================================================================================

    private sealed record Options
    {
        public string Out { get; init; } = "";
        public int Seed { get; init; } = 1;
        public int Contexts { get; init; } = 300;
        public int Full { get; init; } = 150;
        public int MinFull { get; init; } = 100;
        public int Workers { get; init; } = MostWorkers;
        public double BudgetMinutes { get; init; } = 45;
        public double StopAfterMinutes { get; init; } = double.PositiveInfinity;
        public bool GreedyAll { get; init; } = true;
        public int PilotJobs { get; init; } = 200;
        public bool SummarizeOnly { get; init; }

        /// <summary>For a worker process: it plays every ShardCount-th job, from job ShardIndex on. 0 for the process that starts the workers.</summary>
        public int ShardIndex { get; init; }
        public int ShardCount { get; init; }

        /// <summary>For a worker process: "pilot" or "run".</summary>
        public string Phase { get; init; } = "run";

        /// <summary>The additions to play, by change name without "add:" ("Break", "StrikeIronclad+"), comma separated; empty for every card of the pool.</summary>
        public string Cards { get; init; } = "";

        /// <summary>How many times every fight is also played with the same deck in another order: a change that changes nothing but the draw.</summary>
        public int Aa { get; init; }

        /// <summary>"context:change:player": play that one fight and print it decision by decision.</summary>
        public string Trace { get; init; } = "";

        /// <summary>The searching player, by a name <see cref="Policies.ByName"/> knows.</summary>
        public string Player { get; init; } = "Lookahead";

        /// <summary>The tiers played, comma separated.</summary>
        public string Tiers { get; init; } = "normal,elite,boss";

        /// <summary>Only one more Strike and the named additions, on the fights that get additions.</summary>
        public bool OnlyAdds { get; init; }
    }

    private static Options Parse(IReadOnlyList<string> args)
    {
        var o = new Options();
        for (var i = 0; i < args.Count; i++)
        {
            string Value() => i + 1 < args.Count ? args[++i] : throw new UsageException($"{args[i]} needs a value.");
            int Whole(int min, int max)
            {
                var name = args[i];
                var text = Value();
                return int.TryParse(text, NumberStyles.None, Inv, out var n) && n >= min && n <= max
                    ? n : throw new UsageException($"{name} takes a whole number from {min} to {max}, not '{text}'.");
            }
            double Number()
            {
                var name = args[i];
                var text = Value();
                return double.TryParse(text, NumberStyles.AllowDecimalPoint, Inv, out var n) && n > 0
                    ? n : throw new UsageException($"{name} takes a number above 0, not '{text}'.");
            }
            switch (args[i])
            {
                case "--out": o = o with { Out = Value() }; break;
                case "--seed": o = o with { Seed = Whole(0, int.MaxValue) }; break;
                case "--contexts": o = o with { Contexts = Whole(1, 5000) }; break;
                case "--full": o = o with { Full = Whole(1, 5000) }; break;
                case "--min-full": o = o with { MinFull = Whole(1, 5000) }; break;
                case "--workers": o = o with { Workers = Whole(1, MostWorkers) }; break;
                case "--budget-minutes": o = o with { BudgetMinutes = Number() }; break;
                case "--stop-after-minutes": o = o with { StopAfterMinutes = Number() }; break;
                case "--pilot-jobs": o = o with { PilotJobs = Whole(10, 5000) }; break;
                case "--no-greedy-all": o = o with { GreedyAll = false }; break;
                case "--summarize": o = o with { SummarizeOnly = true }; break;
                case "--phase": o = o with { Phase = Value() }; break;
                case "--cards": o = o with { Cards = Value() }; break;
                case "--aa": o = o with { Aa = Whole(0, 9) }; break;
                case "--trace": o = o with { Trace = Value() }; break;
                case "--player":
                {
                    var name = Value();
                    if (Policies.ByName(name) is not IPolicy known) throw new UsageException($"There's no player called '{name}'.");
                    o = o with { Player = known.Name };
                    break;
                }
                case "--tiers":
                {
                    var tiers = Value();
                    var named = tiers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (named.Length == 0 || named.Any(t => t is not (Scenarios.NormalTier or Scenarios.EliteTier or Scenarios.BossTier)))
                        throw new UsageException("--tiers takes normal, elite and boss, separated by commas.");
                    o = o with { Tiers = string.Join(',', named) };
                    break;
                }
                case "--only-adds": o = o with { OnlyAdds = true }; break;
                case "--shard":
                {
                    var parts = Value().Split('/');
                    if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, Inv, out var index) || !int.TryParse(parts[1], NumberStyles.None, Inv, out var count)
                        || count < 1 || count > MostWorkers || index >= count)
                        throw new UsageException("--shard takes i/n, the worker's number and how many there are.");
                    o = o with { ShardIndex = index, ShardCount = count };
                    break;
                }
                default: throw new UsageException($"values has no {args[i]} option.");
            }
        }
        if (o.Trace.Length > 0) return o;
        if (o.Out.Length == 0) throw new UsageException("values needs --out DIR, the folder its tables go to.");
        o = o with { Full = Math.Min(o.Full, o.Contexts) };
        return o with { MinFull = Math.Min(o.MinFull, o.Full), Out = Path.GetFullPath(o.Out) };
    }

    // =====================================================================================
    // The design: contexts, changes, jobs
    // =====================================================================================

    /// <summary>One generated fight. <paramref name="Round"/> is its number within its tier, from 0.</summary>
    private sealed record Ctx(int Round, string Tier, Scenario Scenario, FightSetup Setup)
    {
        public string Id => Scenario.Id;
    }

    /// <summary>One change to a deck. Kinds: base, add, addup (added upgraded), remove, upgrade.</summary>
    private sealed record Change(string Id, string Kind, string Card)
    {
        public static readonly Change Base = new(BaseId, "base", "");
        public static Change Add(string card) => new("add:" + card, "add", card);
        public static Change AddUpgraded(string card) => new("add:" + card + "+", "addup", card);
        public static Change Remove(string card) => new("remove:" + card, "remove", card);
        public static Change Upgrade(string card) => new("upgrade:" + card, "upgrade", card);

        /// <summary>The same deck with its list in another order (the k-th): nothing changes but the shuffle that comes out.</summary>
        public static Change Reorder(int k) => new("reorder:" + k.ToString(Inv), "reorder", k.ToString(Inv));

        public static Change FromId(string id)
        {
            if (id == BaseId) return Base;
            var colon = id.IndexOf(':');
            var (verb, card) = (id[..colon], id[(colon + 1)..]);
            return verb switch
            {
                "add" => card.EndsWith('+') ? AddUpgraded(card[..^1]) : Add(card),
                "remove" => Remove(card),
                "reorder" => Reorder(int.Parse(card, Inv)),
                _ => Upgrade(card),
            };
        }
    }

    private sealed record Job(Ctx Ctx, Change Change, bool Lookahead, bool Greedy);

    /// <summary>
    /// The first <paramref name="perTier"/> normal, elite and boss fights of the arena's generator for the seed.
    /// (Its pattern deals 4 normal, 2 elite, 2 weak and 2 boss fights in every 10; the weak ones are skipped.)
    /// Elites and bosses are then put in turns by enemy (the first fight against each, the second against
    /// each, and so on), so that the fights that get every addition hold each enemy equally often. The
    /// generator's own mix is even too, so this changes the order and not the kind of fights.
    /// </summary>
    private static List<Ctx> BuildContexts(int perTier, int seed)
    {
        var scenarios = Scenarios.Generate(perTier * 5, seed);
        var contexts = new List<Ctx>();
        foreach (var tier in TierOrder)
        {
            var inTier = scenarios.Where(s => s.Tier == tier).Take(perTier).Select(s => (Scenario: s, Setup: Scenarios.Setup(s))).ToList();
            if (inTier.Count < perTier) throw new InvalidOperationException($"The generator gave only {inTier.Count} {tier} fights of {perTier}.");
            if (tier != Scenarios.NormalTier)
            {
                var byEnemy = inTier.GroupBy(x => x.Setup.Encounter, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.ToList()).ToList();
                inTier = Enumerable.Range(0, byEnemy.Max(list => list.Count)).SelectMany(i => byEnemy.Where(list => i < list.Count).Select(list => list[i])).ToList();
            }
            for (var round = 0; round < inTier.Count; round++)
                contexts.Add(new Ctx(round, tier, inTier[round].Scenario, inTier[round].Setup));
        }
        return contexts;
    }

    /// <summary>The changes every context gets: the fight as it is, one more Strike, one more Defend,
    /// one Strike less, one Defend less, and one upgrade for every kind of card in the deck that has a plain copy.</summary>
    private static List<Change> LightChanges(Ctx ctx)
    {
        // The lean design: only the Strike every addition is compared with (a Defend, when asked for, is an addition there).
        if (_onlyAdds) return [Change.Add(Strike)];
        var list = new List<Change> { Change.Base, Change.Add(Strike), Change.Add(Defend) };
        if (ctx.Setup.Deck.Any(c => c.ClassName == Strike)) list.Add(Change.Remove(Strike));
        if (ctx.Setup.Deck.Any(c => c.ClassName == Defend)) list.Add(Change.Remove(Defend));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in ctx.Setup.Deck)
            if (!card.Upgraded && Content.Registry.Card(card.ClassName).MaxUpgradeLevel > 0 && seen.Add(card.ClassName))
                list.Add(Change.Upgrade(card.ClassName));
        for (var k = 1; k <= _aa; k++) list.Add(Change.Reorder(k));
        return list;
    }

    // What --cards and --aa asked for (or what the folder's run.json says they were), for this process.
    private static string _cards = "";
    private static int _aa;
    private static List<Change>? _fullChanges;

    private static void Configure(string cards, int aa, string player = "Lookahead", string tiers = "normal,elite,boss", bool onlyAdds = false)
    {
        _cards = cards ?? "";
        _aa = aa;
        _fullChanges = null;
        LookaheadName = Policies.ByName(string.IsNullOrEmpty(player) ? "Lookahead" : player)?.Name ?? throw new UsageException($"There's no player called '{player}'.");
        TierOrder = (string.IsNullOrEmpty(tiers) ? "normal,elite,boss" : tiers).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _onlyAdds = onlyAdds;
    }

    private static List<Change> BuildFullChanges()
    {
        var list = new List<Change>();
        if (_cards.Length > 0)
        {
            foreach (var name in _cards.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var upgraded = name.EndsWith('+');
                var card = upgraded ? name[..^1] : name;
                var def = Content.Registry.Card(card);
                if (upgraded && def.MaxUpgradeLevel == 0) throw new UsageException($"{card} cannot be upgraded.");
                // Strike and Defend added plain are light changes already (in the lean design only the Strike is).
                if (!upgraded && (card == Strike || (card == Defend && !_onlyAdds))) continue;
                var change = upgraded ? Change.AddUpgraded(card) : Change.Add(card);
                if (list.All(c => c.Id != change.Id)) list.Add(change);
            }
            return list;
        }
        foreach (var def in Content.Registry.IroncladPool)
        {
            if (def.ClassName is not (Strike or Defend)) list.Add(Change.Add(def.ClassName));
            if (def.MaxUpgradeLevel > 0) list.Add(Change.AddUpgraded(def.ClassName));
        }
        return list;
    }

    /// <summary>Every addition a "full" context gets on top of its light changes: each pool card plain
    /// (Strike and Defend are light changes already) and each upgraded; or only the ones --cards names.</summary>
    private static List<Change> FullChanges => _fullChanges ??= BuildFullChanges();

    /// <summary>The deck after the change, or null when the change cannot be made to it.</summary>
    private static List<DeckCard>? Apply(IReadOnlyList<DeckCard> deck, Change change, int seed = 0)
    {
        var list = deck.ToList();
        switch (change.Kind)
        {
            case "base":
                return list;
            case "reorder":
                // The same cards in another list order: the fight's own shuffle then deals another draw order.
                new GameRng(unchecked((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 1000UL * ulong.Parse(change.Card, Inv) + 12345UL)).Shuffle(list);
                return list;
            case "add":
                list.Add(new DeckCard(change.Card));
                return list;
            case "addup":
                list.Add(new DeckCard(change.Card, true));
                return list;
            case "remove":
            {
                // As the generator's shop does it: a plain copy before an upgraded one.
                var i = list.FindLastIndex(c => c.ClassName == change.Card && !c.Upgraded);
                if (i < 0) i = list.FindLastIndex(c => c.ClassName == change.Card);
                if (i < 0) return null;
                list.RemoveAt(i);
                return list;
            }
            case "upgrade":
            {
                var i = list.FindIndex(c => c.ClassName == change.Card && !c.Upgraded);
                if (i < 0) return null;
                list[i] = list[i] with { Upgraded = true };
                return list;
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// The jobs in the order they are played: first the light changes of every context, then
    /// the additions round by round (context r of each tier), so that a run that is cut short
    /// has whole contexts; last, Greedy alone on the contexts Lookahead does not get.
    /// </summary>
    private static List<Job> Plan(List<Ctx> contexts, int full, bool greedyAll)
    {
        var jobs = new List<Job>();
        var rounds = contexts.Max(c => c.Round) + 1;
        IEnumerable<Ctx> Round(int r) => TierOrder.SelectMany(t => contexts.Where(c => c.Tier == t && c.Round == r));
        var byRound = Enumerable.Range(0, rounds).Select(r => Round(r).ToList()).ToList();
        if (_onlyAdds)
        {
            // Round by round, the Strike first: a run that is cut short holds whole fights with their reference.
            for (var r = 0; r < rounds && r < full; r++)
                foreach (var ctx in byRound[r])
                    foreach (var change in LightChanges(ctx).Concat(FullChanges))
                        jobs.Add(new Job(ctx, change, true, false));
            return jobs;
        }
        foreach (var round in byRound)
            foreach (var ctx in round)
                foreach (var change in LightChanges(ctx))
                    jobs.Add(new Job(ctx, change, true, true));
        for (var r = 0; r < rounds && r < full; r++)
            foreach (var ctx in byRound[r])
                foreach (var change in FullChanges)
                    jobs.Add(new Job(ctx, change, true, true));
        if (greedyAll)
            for (var r = full; r < rounds; r++)
                foreach (var ctx in byRound[r])
                    foreach (var change in FullChanges)
                        jobs.Add(new Job(ctx, change, false, true));
        return jobs;
    }

    // =====================================================================================
    // One fight
    // =====================================================================================

    /// <summary>One fight's outcome, one line of fights.csv.</summary>
    private sealed record FightRow
    {
        public required string Context { get; init; }
        public required string Tier { get; init; }
        public required int Round { get; init; }
        public required string Encounter { get; init; }
        public required string Change { get; init; }
        public required string Policy { get; init; }
        public bool Won { get; init; }

        /// <summary>HP after the fight, Burning Blood's 6 included; 0 for a loss, also one by the turn limit.</summary>
        public int HpLeft { get; init; }
        public int Turns { get; init; }
        public int HpStart { get; init; }
        public int MaxHp { get; init; }
        public int DamageTaken { get; init; }
        public int EnemyHpLeft { get; init; }
        public bool TimedOut { get; init; }
        public int Refused { get; init; }
        public int CardsPlayed { get; init; }

        /// <summary>The change's card: turns with a copy in hand at a decision, turns in which one was
        /// played from the hand, plays from the hand, and the first turn of each (0 for never).</summary>
        public int HandTurns { get; init; }
        public int PlayTurns { get; init; }
        public int Plays { get; init; }
        public int FirstHandTurn { get; init; }
        public int FirstPlayTurn { get; init; }
        public int Ms { get; init; }
        public bool Error { get; init; }

        /// <summary>The change's card by the kind of turn: turns it was in the hand and could be played, and turns it
        /// was played in, by what was coming through the Block already there at the turn's first decision
        /// (none: 0; small: under <see cref="LargeDanger"/>; large: that or more).</summary>
        public int HoldNone { get; init; }
        public int HoldSmall { get; init; }
        public int HoldLarge { get; init; }
        public int PlayNone { get; init; }
        public int PlaySmall { get; init; }
        public int PlayLarge { get; init; }

        public const string Header =
            "context,tier,round,encounter,change,policy,won,hp_left,turns,hp_start,max_hp,damage_taken,enemy_hp_left," +
            "timed_out,refused,cards_played,x_hand_turns,x_play_turns,x_plays,x_first_hand_turn,x_first_play_turn,ms,error," +
            "x_hold_none,x_hold_small,x_hold_large,x_play_none,x_play_small,x_play_large";

        public string ToCsv() => string.Create(Inv,
            $"{Context},{Tier},{Round},{Encounter},{Change},{Policy},{(Won ? 1 : 0)},{HpLeft},{Turns},{HpStart},{MaxHp},{DamageTaken},{EnemyHpLeft}," +
            $"{(TimedOut ? 1 : 0)},{Refused},{CardsPlayed},{HandTurns},{PlayTurns},{Plays},{FirstHandTurn},{FirstPlayTurn},{Ms},{(Error ? 1 : 0)}," +
            $"{HoldNone},{HoldSmall},{HoldLarge},{PlayNone},{PlaySmall},{PlayLarge}");

        public static FightRow? Parse(string line)
        {
            var f = line.Split(',');
            // 23 fields in the logs of the first measurement, 29 since the kinds of turn are logged.
            if (f.Length != 23 && f.Length != 29) return null;
            try
            {
                int I(int i) => int.Parse(f[i], Inv);
                return new FightRow
                {
                    Context = f[0], Tier = f[1], Round = I(2), Encounter = f[3], Change = f[4], Policy = f[5],
                    Won = f[6] == "1", HpLeft = I(7), Turns = I(8), HpStart = I(9), MaxHp = I(10), DamageTaken = I(11), EnemyHpLeft = I(12),
                    TimedOut = f[13] == "1", Refused = I(14), CardsPlayed = I(15),
                    HandTurns = I(16), PlayTurns = I(17), Plays = I(18), FirstHandTurn = I(19), FirstPlayTurn = I(20), Ms = I(21), Error = f[22] == "1",
                    HoldNone = f.Length > 23 ? I(23) : 0, HoldSmall = f.Length > 23 ? I(24) : 0, HoldLarge = f.Length > 23 ? I(25) : 0,
                    PlayNone = f.Length > 23 ? I(26) : 0, PlaySmall = f.Length > 23 ? I(27) : 0, PlayLarge = f.Length > 23 ? I(28) : 0,
                };
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public string Key => KeyOf(Context, Change, Policy);
    }

    private static string KeyOf(string context, string change, string policy) => context + "|" + change + "|" + policy;

    private static FightRow Play(Ctx ctx, Change change, IPolicy policy)
    {
        var watch = Stopwatch.StartNew();
        var row = new FightRow
        {
            Context = ctx.Id, Tier = ctx.Tier, Round = ctx.Round, Encounter = ctx.Setup.Encounter, Change = change.Id, Policy = policy.Name,
        };
        try
        {
            var deck = Apply(ctx.Setup.Deck, change, ctx.Scenario.Seed) ?? throw new InvalidOperationException($"{change.Id} cannot be made to the deck of {ctx.Id}.");
            var game = new ArenaGame(ctx.Scenario, ctx.Setup with { Deck = deck }) { ActionDelay = TimeSpan.Zero };
            var fight = game.Fight;
            var hpAtStart = game.PlayerHp;
            var tracked = change.Kind is "base" or "reorder" ? null : change.Card;
            int handTurns = 0, playTurns = 0, plays = 0, firstHand = 0, firstPlay = 0, lastHandTurn = 0, lastPlayTurn = 0;
            // The kind of turn: 0 none, 1 small, 2 large, worked out at the turn's first decision.
            int dangerTurn = 0, danger = 0, lastHoldTurn = 0, lastDangerPlayTurn = 0;
            var hold = new int[3];
            var playedIn = new int[3];

            for (var n = 0; n < MostDecisions && !game.IsOver; n++)
            {
                var snapshot = game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
                var inCombat = snapshot.Screen == "combat";
                var turn = fight.Turn;
                if (tracked is not null && inCombat && turn != lastHandTurn && fight.Hand.Any(c => c.Def.ClassName == tracked))
                {
                    handTurns++;
                    lastHandTurn = turn;
                    if (firstHand == 0) firstHand = turn;
                }
                if (inCombat && turn != dangerTurn)
                {
                    dangerTurn = turn;
                    var through = Math.Max(0, fight.IncomingDamage() - fight.Player.Block);
                    danger = through == 0 ? 0 : through < LargeDanger ? 1 : 2;
                }
                if (tracked is not null && inCombat && turn != lastHoldTurn && fight.Hand.Any(c => c.Def.ClassName == tracked && fight.CanPlay(c, out _)))
                {
                    hold[danger]++;
                    lastHoldTurn = turn;
                }

                var choice = policy is IEnginePolicy searching ? searching.Choose(snapshot, fight) : policy.Choose(snapshot);
                var playsTracked = tracked is not null && inCombat && PlayedHandIndex(choice) is { } index
                                   && index < fight.Hand.Count && fight.Hand[index].Def.ClassName == tracked;
                var result = game.ExecuteAsync(choice, CancellationToken.None).GetAwaiter().GetResult();
                if (result.Ok)
                {
                    if (!playsTracked) continue;
                    plays++;
                    if (turn != lastDangerPlayTurn)
                    {
                        // A card drawn and played inside one decision's line was never seen "in the hand and playable": count it as held too.
                        if (turn != lastHoldTurn)
                        {
                            hold[danger]++;
                            lastHoldTurn = turn;
                        }
                        playedIn[danger]++;
                        lastDangerPlayTurn = turn;
                    }
                    if (turn != lastPlayTurn)
                    {
                        playTurns++;
                        lastPlayTurn = turn;
                    }
                    if (firstPlay == 0) firstPlay = turn;
                    continue;
                }

                // As the baseline does it: a refused action gives up the turn, or takes the first offer inside a choice.
                var fallback = snapshot.Actions.Any(a => a.Id == "end_turn") ? "end_turn" : snapshot.Actions[0].Id;
                game.ExecuteAsync(fallback, CancellationToken.None).GetAwaiter().GetResult();
            }

            return row with
            {
                Won = game.Won, HpLeft = game.Won ? game.PlayerHp : 0, Turns = game.Turn, HpStart = hpAtStart, MaxHp = game.PlayerMaxHp,
                DamageTaken = game.DamageTaken, EnemyHpLeft = game.EnemyHpLeft, TimedOut = game.TimedOut, Refused = game.RefusedActions,
                CardsPlayed = game.CardsPlayed, HandTurns = handTurns, PlayTurns = playTurns, Plays = plays,
                FirstHandTurn = firstHand, FirstPlayTurn = firstPlay, Ms = (int)watch.ElapsedMilliseconds,
                HoldNone = hold[0], HoldSmall = hold[1], HoldLarge = hold[2], PlayNone = playedIn[0], PlaySmall = playedIn[1], PlayLarge = playedIn[2],
            };
        }
        catch (Exception)
        {
            return row with { Error = true, Ms = (int)watch.ElapsedMilliseconds };
        }
    }

    /// <summary>Plays one fight of the measurement again and prints it decision by decision: "elite-123:add:Bash:Lookahead".</summary>
    private static int Trace(string what, TextWriter o)
    {
        var first = what.IndexOf(':');
        var last = what.LastIndexOf(':');
        if (first < 0 || last <= first) throw new UsageException("--trace takes context:change:player, as in elite-123:add:Bash:Lookahead.");
        var scenario = Scenarios.FromId(what[..first]);
        var setup = Scenarios.Setup(scenario);
        // A player's own name may hold a colon (Rollout:fast): then the change is what stands between the first colon and the player.
        var policy = Policies.ByName(what[(last + 1)..]);
        if (policy is null && what.LastIndexOf(':', last - 1) is var before && before > first && Policies.ByName(what[(before + 1)..]) is { } named)
        {
            policy = named;
            last = before;
        }
        if (policy is null) throw new UsageException($"There's no player called '{what[(last + 1)..]}'.");
        var change = Change.FromId(what[(first + 1)..last]);
        var deck = Apply(setup.Deck, change, scenario.Seed) ?? throw new UsageException($"{change.Id} cannot be made to the deck of {scenario.Id}.");
        var game = new ArenaGame(scenario, setup with { Deck = deck }) { ActionDelay = TimeSpan.Zero };
        var fight = game.Fight;
        o.WriteLine($"{scenario.Id} {change.Id} {policy.Name}: {scenario.Summary}");
        o.WriteLine("deck: " + string.Join(' ', deck.Select(d => d.ToString())));
        o.WriteLine("draw pile after the first hand: " + string.Join(", ", fight.DrawPile.Select(c => c.Title)));
        var lastTurn = 0;
        for (var n = 0; n < MostDecisions && !game.IsOver; n++)
        {
            var snapshot = game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (fight.Turn != lastTurn)
            {
                lastTurn = fight.Turn;
                static string PowersOf(Creature c) => string.Join(", ", c.Powers.Where(p => p.IsVisible).Select(p => p.ClassName.Replace("Power", "") + " " + p.Amount.ToString(Inv)));
                o.WriteLine($"-- turn {fight.Turn}: HP {fight.Player.Hp}, Block {fight.Player.Block}, powers [{PowersOf(fight.Player)}]; enemies " +
                            string.Join("; ", fight.Enemies.Where(e => e.IsAlive).Select(e => $"{e.Name} {e.Hp}hp {e.Block}blk [{PowersOf(e)}] next {e.Monster?.NextMove?.Id}")));
            }
            var choice = policy is IEnginePolicy searching ? searching.Choose(snapshot, fight) : policy.Choose(snapshot);
            var label = snapshot.Actions.FirstOrDefault(a => a.Id == choice)?.Label ?? "?";
            o.WriteLine(string.Create(Inv, $"   [{snapshot.Screen}] energy {fight.Energy}, hand: {string.Join(", ", fight.Hand.Select(c => c.Title + "(" + fight.CostText(c) + ")"))}  ->  {choice} ({label})"));
            var result = game.ExecuteAsync(choice, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Ok) continue;
            var fallback = snapshot.Actions.Any(a => a.Id == "end_turn") ? "end_turn" : snapshot.Actions[0].Id;
            o.WriteLine($"   refused; {fallback} instead");
            game.ExecuteAsync(fallback, CancellationToken.None).GetAwaiter().GetResult();
        }
        o.WriteLine(string.Create(Inv, $"== {(game.Won ? "won" : game.TimedOut ? "lost to the turn limit" : "lost")} on turn {game.Turn}: HP left {(game.Won ? game.PlayerHp : 0)}, damage taken {game.DamageTaken}, enemy HP left {game.EnemyHpLeft}, cards played {game.CardsPlayed}"));
        return 0;
    }

    /// <summary>"play:3" and "play:3>e1" as 3; null for any other action.</summary>
    private static int? PlayedHandIndex(string action)
    {
        const string prefix = "play:";
        if (!action.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var arrow = action.IndexOf('>');
        var digits = arrow < 0 ? action.AsSpan(prefix.Length) : action.AsSpan(prefix.Length, arrow - prefix.Length);
        return int.TryParse(digits, NumberStyles.None, Inv, out var index) ? index : null;
    }

    // =====================================================================================
    // The run
    // =====================================================================================

    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        Options options;
        try
        {
            options = Parse(args);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage:\n" + Usage);
            return 2;
        }

        if (options.Trace.Length > 0) return Trace(options.Trace, output);
        Directory.CreateDirectory(options.Out);
        if (options.SummarizeOnly)
        {
            var settings = ReadSettings(options.Out) ?? throw new InvalidOperationException($"{options.Out} has no run.json: nothing was run there.");
            Configure(settings.Cards, settings.Aa, settings.Player, settings.Tiers, settings.OnlyAdds);
            var contexts = BuildContexts(settings.Contexts, settings.Seed);
            if (settings.OnlyAdds) SummarizeAdds(options.Out, contexts, LoadLogs(options.Out), settings, output);
            else Summarize(options.Out, contexts, LoadLogs(options.Out), settings, output);
            return 0;
        }
        Configure(options.Cards, options.Aa, options.Player, options.Tiers, options.OnlyAdds);
        return options.ShardCount > 0 ? RunWorker(options) : RunParent(options, args, output);
    }

    /// <summary>The pilot's jobs: the same number per tier, drawn from the plan of the smallest design
    /// the run may end up with, so that none of them is played for nothing. The same list in every process.</summary>
    private static List<Job> PilotJobs(List<Ctx> contexts, Options options)
    {
        var floorPlan = Plan(contexts, options.MinFull, greedyAll: false);
        var pick = new GameRng(unchecked((ulong)options.Seed * 0x9E3779B97F4A7C15UL + 77));
        var pilot = new List<Job>();
        foreach (var tier in TierOrder)
        {
            var pool = floorPlan.Where(j => j.Ctx.Tier == tier).ToList();
            pick.Shuffle(pool);
            pilot.AddRange(pool.Take(options.PilotJobs));
        }
        pick.Shuffle(pilot);
        return pilot;
    }

    /// <summary>
    /// One worker process: it takes every n-th job of the phase's list, plays what no log holds
    /// yet, one fight at a time, and appends to its own log. Processes, not threads: six
    /// threads in one process spend half their time waiting for the garbage collector.
    /// </summary>
    private static int RunWorker(Options o)
    {
        var contexts = BuildContexts(o.Contexts, o.Seed);
        var jobs = o.Phase == "pilot" ? PilotJobs(contexts, o) : Plan(contexts, o.Full, o.GreedyAll);
        var doneKeys = LoadLogs(o.Out).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var policies = new Dictionary<string, IPolicy>(StringComparer.Ordinal)
        {
            [LookaheadName] = Policies.ByName(LookaheadName) ?? throw new InvalidOperationException($"No player called {LookaheadName}."),
            [GreedyName] = Policies.All.First(p => p.Name == GreedyName),
        };
        var path = Path.Combine(o.Out, string.Create(Inv, $"fights.{o.ShardIndex}.csv"));
        EndWithNewline(path);
        using var log = new StreamWriter(path, append: true, Utf8);
        if (new FileInfo(path).Length == 0) log.WriteLine(FightRow.Header);
        var stop = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Volatile.Write(ref stop, true);
        };
        var started = Stopwatch.StartNew();
        var sinceFlush = Stopwatch.StartNew();
        for (var j = o.ShardIndex; j < jobs.Count; j += o.ShardCount)
        {
            if (Volatile.Read(ref stop) || started.Elapsed.TotalMinutes >= o.StopAfterMinutes) break;
            var job = jobs[j];
            foreach (var (name, wanted) in new[] { (LookaheadName, job.Lookahead), (GreedyName, job.Greedy) })
            {
                if (!wanted || !doneKeys.Add(KeyOf(job.Ctx.Id, job.Change.Id, name))) continue;
                log.WriteLine(Play(job.Ctx, job.Change, policies[name]).ToCsv());
            }
            if (sinceFlush.ElapsedMilliseconds < 2000) continue;
            log.Flush();
            sinceFlush.Restart();
        }
        log.Flush();
        return 0;
    }

    private static int RunParent(Options options, IReadOnlyList<string> args, TextWriter output)
    {
        var started = Stopwatch.StartNew();
        output.WriteLine($"Card values: {options.Contexts} fights of each tier ({string.Join(", ", TierOrder)}) from seed {options.Seed}; {options.Workers} worker processes; the searching player is {LookaheadName}.");
        var contexts = BuildContexts(options.Contexts, options.Seed);
        var before = LoadLogs(options.Out);
        if (before.Count > 0) output.WriteLine($"{options.Out} holds {before.Count} fights already; they are not played again.");

        var stop = false;
        var children = new List<Process>();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Volatile.Write(ref stop, true);
        };

        // Mean seconds of one Lookahead fight per tier, from every fight the logs hold.
        static Dictionary<string, (double Seconds, int Count)> Timing(List<FightRow> rows) =>
            TierOrder.ToDictionary(t => t, t =>
            {
                var ms = rows.Where(r => r.Policy == LookaheadName && r.Tier == t && !r.Error).Select(r => (double)r.Ms).ToList();
                return (ms.Count > 0 ? ms.Average() / 1000 : double.NaN, ms.Count);
            });

        // What is left of a job list, as Lookahead fights by tier (Greedy's cost is small change next to it).
        static Dictionary<string, int> LeftByTier(IEnumerable<Job> jobs, HashSet<string> doneKeys)
        {
            var left = TierOrder.ToDictionary(t => t, _ => 0);
            foreach (var job in jobs)
                if (job.Lookahead && !doneKeys.Contains(KeyOf(job.Ctx.Id, job.Change.Id, LookaheadName)))
                    left[job.Ctx.Tier]++;
            return left;
        }

        double SecondsFor(Dictionary<string, int> left, Dictionary<string, (double Seconds, int Count)> timing) =>
            left.Sum(kv => kv.Value * (double.IsNaN(timing[kv.Key].Seconds) ? 0.3 : timing[kv.Key].Seconds)) / options.Workers;

        // Starts the worker processes for a phase and waits for them, printing how far they are.
        void RunPhase(string phase, int full, List<Job> jobs, string what)
        {
            var phaseWatch = Stopwatch.StartNew();
            var stopAfter = options.StopAfterMinutes - started.Elapsed.TotalMinutes;
            if (stopAfter <= 0 || Volatile.Read(ref stop)) return;
            children.Clear();
            for (var i = 0; i < options.Workers; i++)
            {
                var info = new ProcessStartInfo { FileName = Environment.ProcessPath!, UseShellExecute = false };
                if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    info.ArgumentList.Add(typeof(ValuesCommand).Assembly.Location);
                foreach (var arg in new[]
                         {
                             "values", "--out", options.Out, "--seed", options.Seed.ToString(Inv), "--contexts", options.Contexts.ToString(Inv),
                             "--full", full.ToString(Inv), "--min-full", options.MinFull.ToString(Inv), "--pilot-jobs", options.PilotJobs.ToString(Inv),
                             "--shard", string.Create(Inv, $"{i}/{options.Workers}"), "--phase", phase,
                         })
                    info.ArgumentList.Add(arg);
                if (!options.GreedyAll) info.ArgumentList.Add("--no-greedy-all");
                if (options.Cards.Length > 0)
                {
                    info.ArgumentList.Add("--cards");
                    info.ArgumentList.Add(options.Cards);
                }
                if (options.Aa > 0)
                {
                    info.ArgumentList.Add("--aa");
                    info.ArgumentList.Add(options.Aa.ToString(Inv));
                }
                info.ArgumentList.Add("--player");
                info.ArgumentList.Add(LookaheadName);
                info.ArgumentList.Add("--tiers");
                info.ArgumentList.Add(options.Tiers);
                if (options.OnlyAdds) info.ArgumentList.Add("--only-adds");
                if (!double.IsPositiveInfinity(stopAfter))
                {
                    info.ArgumentList.Add("--stop-after-minutes");
                    info.ArgumentList.Add(stopAfter.ToString("0.###", Inv));
                }
                // One thread each: no background collector next to the fight.
                info.Environment["DOTNET_gcServer"] = "0";
                info.Environment["DOTNET_gcConcurrent"] = "0";
                children.Add(Process.Start(info)!);
            }

            var lastPrint = Stopwatch.StartNew();
            while (children.Any(c => !c.HasExited))
            {
                Thread.Sleep(500);
                if (Volatile.Read(ref stop))
                {
                    foreach (var c in children.Where(c => !c.HasExited))
                        try { c.Kill(); } catch (Exception) { /* it ended by itself */ }
                    break;
                }
                if (lastPrint.Elapsed.TotalSeconds < 30) continue;
                lastPrint.Restart();
                var rows = LoadLogs(options.Out);
                var keys = rows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
                var left = LeftByTier(jobs, keys);
                var total = jobs.Count(j => j.Lookahead);
                output.WriteLine(string.Create(Inv,
                    $"  [{started.Elapsed:hh\\:mm\\:ss}] {what}: {total - left.Values.Sum()}/{total} {LookaheadName} fights done, about {SecondsFor(left, Timing(rows)) / 60:0.0} min left"));
            }
            foreach (var c in children) c.WaitForExit();
            var failed = children.Count(c => c.ExitCode != 0);
            output.WriteLine(string.Create(Inv, $"  {what}: {phaseWatch.Elapsed.TotalMinutes:0.0} min") + (failed > 0 ? $"; {failed} worker processes ended badly (run the same command again to carry on)." : "."));
        }

        // ---- the pilot: a sample of the real jobs, to learn what a fight of each tier costs here and now ----
        if (!options.OnlyAdds && Timing(before).Values.Any(v => v.Count < options.PilotJobs))
        {
            var pilot = PilotJobs(contexts, options);
            output.WriteLine($"Pilot: {pilot.Count} jobs drawn at random from the plan, to time a fight of each tier with {options.Workers} processes busy.");
            RunPhase("pilot", options.MinFull, pilot, "pilot");
        }
        var afterPilot = LoadLogs(options.Out);
        var timing = Timing(afterPilot);
        var doneKeys = afterPilot.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        output.WriteLine("One Lookahead fight takes: " +
                         string.Join(", ", TierOrder.Select(t => string.Create(Inv, $"{t} {timing[t].Seconds:0.000} s ({timing[t].Count} timed)"))) + ".");

        // ---- the size of the run: as many full contexts per tier as the budget allows, never below the floor ----
        var budgetLeft = options.BudgetMinutes * 60 - started.Elapsed.TotalSeconds;
        double Estimate(int k) => SecondsFor(LeftByTier(Plan(contexts, k, greedyAll: false), doneKeys), timing);
        var table = new TextTable("additions on", "Lookahead fights left", "estimated minutes");
        foreach (var k in new[] { options.Full, (options.Full + options.MinFull) / 2, options.MinFull }.Distinct())
            table.Add($"{k} fights per tier", LeftByTier(Plan(contexts, k, greedyAll: false), doneKeys).Values.Sum().ToString(Inv), (Estimate(k) / 60).ToString("0.0", Inv));
        table.Write(output, "  ");
        var full = options.Full;
        // The lean design is played at the size asked for: --stop-after-minutes is its only limit.
        while (!options.OnlyAdds && full > options.MinFull && Estimate(full) > budgetLeft * BudgetShare) full--;
        // A folder that already holds more full contexts keeps them.
        var settingsBefore = ReadSettings(options.Out);
        if (settingsBefore is not null && settingsBefore.Full > full) full = Math.Min(settingsBefore.Full, options.Full);
        var estimate = Estimate(full);
        output.WriteLine(string.Create(Inv, $"Budget {options.BudgetMinutes:0} min, {budgetLeft / 60:0.0} left after the pilot. ") +
                         (full < options.Full
                             ? string.Create(Inv, $"The additions are cut from {options.Full} to {full} fights per tier; estimated {estimate / 60:0.0} min.")
                             : string.Create(Inv, $"The whole design fits: additions on {full} fights per tier; estimated {estimate / 60:0.0} min.")) +
                         (estimate > budgetLeft ? " That is over the budget: the design is not cut below its floor." : ""));

        var plan = Plan(contexts, full, options.GreedyAll);
        var settingsNow = new Settings
        {
            Seed = options.Seed, Contexts = options.Contexts, Full = full, FullAsked = options.Full, MinFull = options.MinFull,
            Workers = options.Workers, BudgetMinutes = options.BudgetMinutes, GreedyAll = options.GreedyAll, Cards = options.Cards, Aa = options.Aa,
            Player = LookaheadName, Tiers = options.Tiers, OnlyAdds = options.OnlyAdds,
            LookaheadBudget = LookaheadPolicy.DefaultBudget, LookaheadWorlds = LookaheadPolicy.DefaultWorlds, MaxTurns = Tuning.MaxTurns,
            EstimatedMinutes = estimate / 60, CommandLine = "values " + string.Join(' ', args),
            StartedUtc = DateTime.UtcNow.ToString("u", Inv), MinutesTaken = settingsBefore?.MinutesTaken ?? 0,
        };
        WriteSettings(options.Out, settingsNow);
        WriteContexts(options.Out, contexts, full);

        RunPhase("run", full, plan, "run");

        var all = LoadLogs(options.Out);
        settingsNow = settingsNow with { MinutesTaken = started.Elapsed.TotalMinutes + (settingsBefore?.MinutesTaken ?? 0), Stopped = Volatile.Read(ref stop) };
        WriteSettings(options.Out, settingsNow);
        output.WriteLine(string.Create(Inv, $"Played {all.Count - before.Count} fights in {started.Elapsed.TotalMinutes:0.0} min. The logs are {options.Out}/fights.*.csv."));
        output.WriteLine();
        if (options.OnlyAdds) SummarizeAdds(options.Out, contexts, all, settingsNow, output);
        else Summarize(options.Out, contexts, all, settingsNow, output);
        return 0;
    }

    /// <summary>Every fight of every worker's log in the folder, each (context, change, player) once.</summary>
    private static List<FightRow> LoadLogs(string dir)
    {
        var rows = new List<FightRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(dir, "fights.*.csv").Order(StringComparer.Ordinal))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Utf8);
            while (reader.ReadLine() is { } line)
                if (FightRow.Parse(line) is { } row && seen.Add(row.Key))
                    rows.Add(row);
        }
        return rows;
    }

    /// <summary>A log that was cut off in the middle of a line gets its line end, so that the next row starts a new one.</summary>
    private static void EndWithNewline(string path)
    {
        if (!File.Exists(path)) return;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        if (stream.Length == 0) return;
        stream.Seek(-1, SeekOrigin.End);
        if (stream.ReadByte() != '\n') stream.WriteByte((byte)'\n');
    }

    // =====================================================================================
    // Settings and the context list
    // =====================================================================================

    private sealed record Settings
    {
        public int Seed { get; init; }
        public int Contexts { get; init; }
        public int Full { get; init; }
        public int FullAsked { get; init; }
        public int MinFull { get; init; }
        public int Workers { get; init; }
        public double BudgetMinutes { get; init; }
        public bool GreedyAll { get; init; }
        public int LookaheadBudget { get; init; }
        public int LookaheadWorlds { get; init; }
        public int MaxTurns { get; init; }
        public double EstimatedMinutes { get; init; }
        public double MinutesTaken { get; init; }
        public bool Stopped { get; init; }
        public string CommandLine { get; init; } = "";
        public string StartedUtc { get; init; } = "";

        /// <summary>The additions --cards named; empty for the whole pool.</summary>
        public string Cards { get; init; } = "";

        /// <summary>How many "same deck, other order" fights every context got.</summary>
        public int Aa { get; init; }

        /// <summary>The searching player's name (Lookahead in the first measurement, which did not write this).</summary>
        public string Player { get; init; } = "Lookahead";
        public string Tiers { get; init; } = "normal,elite,boss";
        public bool OnlyAdds { get; init; }
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static void WriteSettings(string dir, Settings settings) =>
        File.WriteAllText(Path.Combine(dir, "run.json"), JsonSerializer.Serialize(settings, Json));

    private static Settings? ReadSettings(string dir)
    {
        var path = Path.Combine(dir, "run.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) : null;
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

    private static void WriteContexts(string dir, List<Ctx> contexts, int full)
    {
        using var w = new StreamWriter(Path.Combine(dir, "contexts.csv"), false, Utf8);
        w.WriteLine("context,tier,round,seed,gets_additions,encounter,floor,hp,max_hp,deck_size,deck,potions,summary");
        foreach (var c in contexts)
            w.WriteLine(string.Create(Inv,
                $"{c.Id},{c.Tier},{c.Round},{c.Scenario.Seed},{(c.Round < full ? 1 : 0)},{c.Setup.Encounter},{c.Setup.Floor},{c.Setup.Hp},{c.Setup.MaxHp},{c.Setup.Deck.Count}," +
                $"{string.Join(' ', c.Setup.Deck.Select(d => d.ToString()))},{string.Join(' ', c.Setup.Potions.Where(p => p is not null))},{Quote(c.Scenario.Summary)}"));
    }

    // =====================================================================================
    // The tables
    // =====================================================================================

    /// <summary>A mean with its 95% interval (Student's t on the values given).</summary>
    private readonly record struct Est(int N, double Mean, double Lo, double Hi, double Se)
    {
        public static readonly Est None = new(0, double.NaN, double.NaN, double.NaN, double.NaN);

        public static Est Of(IReadOnlyList<double> values)
        {
            var n = values.Count;
            if (n == 0) return None;
            var mean = values.Average();
            if (n == 1) return new Est(1, mean, double.NaN, double.NaN, double.NaN);
            var variance = values.Sum(v => (v - mean) * (v - mean)) / (n - 1);
            var se = Math.Sqrt(variance / n);
            var t = T975(n - 1);
            return new Est(n, mean, mean - t * se, mean + t * se, se);
        }

        /// <summary>The sum of a mean over a subset and a mean over the whole set, with the covariance between the two taken into account.</summary>
        public static Est Sum(IReadOnlyList<double> a, IReadOnlyList<double> bOnSame, IReadOnlyList<double> bAll)
        {
            if (a.Count < 2 || bAll.Count < 2) return None;
            double ma = a.Average(), mb = bAll.Average(), mbs = bOnSame.Average();
            var va = a.Sum(v => (v - ma) * (v - ma)) / (a.Count - 1);
            var vb = bAll.Sum(v => (v - mb) * (v - mb)) / (bAll.Count - 1);
            var cov = 0.0;
            for (var i = 0; i < a.Count; i++) cov += (a[i] - ma) * (bOnSame[i] - mbs);
            cov /= a.Count - 1;
            var variance = Math.Max(0, va / a.Count + vb / bAll.Count + 2 * cov / bAll.Count);
            var se = Math.Sqrt(variance);
            const double z = 1.959964;
            return new Est(a.Count, ma + mb, ma + mb - z * se, ma + mb + z * se, se);
        }

        /// <summary>The 97.5% point of Student's t (Cornish-Fisher; good to three places from 5 degrees of freedom up).</summary>
        private static double T975(int df)
        {
            const double z = 1.959964;
            double z3 = z * z * z, z5 = z3 * z * z, z7 = z5 * z * z;
            double d = df;
            return z + (z3 + z) / (4 * d) + (5 * z5 + 16 * z3 + 3 * z) / (96 * d * d) + (3 * z7 + 19 * z5 + 17 * z3 - 15 * z) / (384 * d * d * d);
        }
    }

    /// <summary>What a set of changes did in a set of contexts, per context first (a context that
    /// has several of the changes counts once, with their mean).</summary>
    private sealed record Effect
    {
        public int Pairs { get; init; }
        public double BaseWin { get; init; }
        public double VarWin { get; init; }
        public double BaseHp { get; init; }
        public double VarHp { get; init; }
        public double BaseTurns { get; init; }
        public double VarTurns { get; init; }
        public int Gained { get; init; }
        public int Lost { get; init; }
        public Est Win { get; init; } = Est.None;
        public Est Hp { get; init; } = Est.None;
        public Est Turns { get; init; } = Est.None;
    }

    /// <summary>Everything one policy played, by context and change.</summary>
    private sealed class Results
    {
        private readonly Dictionary<string, Dictionary<string, FightRow>> _byContext = new(StringComparer.Ordinal);

        public Results(IEnumerable<FightRow> rows)
        {
            foreach (var row in rows)
            {
                if (!_byContext.TryGetValue(row.Context, out var changes)) _byContext[row.Context] = changes = new(StringComparer.Ordinal);
                changes[row.Change] = row;
            }
        }

        public FightRow? Get(string context, string change) =>
            _byContext.TryGetValue(context, out var changes) && changes.TryGetValue(change, out var row) && !row.Error ? row : null;

        public bool Has(string context, string change) => _byContext.TryGetValue(context, out var changes) && changes.ContainsKey(change);
    }

    /// <summary>
    /// The paired comparison: for every context of <paramref name="contexts"/> that has the
    /// reference fight and at least one of the changes, the mean outcome over its changes minus
    /// the reference's. <paramref name="reference"/> is "base" or another change's id.
    /// </summary>
    private static (Effect Effect, List<string> Contexts, List<double> Win, List<double> Hp) Compare(
        Results results, IEnumerable<Ctx> contexts, IReadOnlyList<string> changes, string reference = BaseId)
    {
        var used = new List<string>();
        List<double> dWin = new(), dHp = new(), dTurns = new();
        double baseWin = 0, varWin = 0, baseHp = 0, varHp = 0, baseTurns = 0, varTurns = 0;
        int gained = 0, lost = 0, pairs = 0;
        foreach (var ctx in contexts)
        {
            if (results.Get(ctx.Id, reference) is not { } b) continue;
            double win = 0, hp = 0, turns = 0;
            var n = 0;
            foreach (var change in changes)
            {
                if (change == reference || results.Get(ctx.Id, change) is not { } v) continue;
                n++;
                win += v.Won ? 1 : 0;
                hp += v.HpLeft;
                turns += v.Turns;
                if (v.Won && !b.Won) gained++;
                if (!v.Won && b.Won) lost++;
            }
            if (n == 0) continue;
            pairs += n;
            used.Add(ctx.Id);
            win /= n;
            hp /= n;
            turns /= n;
            dWin.Add(100 * (win - (b.Won ? 1 : 0)));
            dHp.Add(hp - b.HpLeft);
            dTurns.Add(turns - b.Turns);
            baseWin += b.Won ? 1 : 0;
            varWin += win;
            baseHp += b.HpLeft;
            varHp += hp;
            baseTurns += b.Turns;
            varTurns += turns;
        }
        var c = Math.Max(1, used.Count);
        var effect = new Effect
        {
            Pairs = pairs, BaseWin = 100 * baseWin / c, VarWin = 100 * varWin / c, BaseHp = baseHp / c, VarHp = varHp / c,
            BaseTurns = baseTurns / c, VarTurns = varTurns / c, Gained = gained, Lost = lost,
            Win = Est.Of(dWin), Hp = Est.Of(dHp), Turns = Est.Of(dTurns),
        };
        return (effect, used, dWin, dHp);
    }

    /// <summary>One line of the summary: a change (or a named group of changes) in a group of fights.</summary>
    private sealed record Line
    {
        public required string Policy { get; init; }
        public required string Sample { get; init; }
        public required string Group { get; init; }
        public required string Label { get; init; }
        public required string Kind { get; init; }
        public string Card { get; init; } = "";
        public string CardName { get; init; } = "";
        public string Type { get; init; } = "";
        public string Rarity { get; init; } = "";
        public string Cost { get; init; } = "";
        public required Effect Direct { get; init; }

        /// <summary>For additions: against the same fight with one more Strike (same shuffle, the card where the Strike would be).</summary>
        public Effect? VsStrike { get; init; }

        /// <summary>For additions: that, plus what one more Strike does against the fight as it is, measured on every context.</summary>
        public Est ViaWin { get; init; } = Est.None;
        public Est ViaHp { get; init; } = Est.None;

        /// <summary>For upgraded additions: against the same card added plain (same shuffle).</summary>
        public Effect? VsPlain { get; init; }

        /// <summary>For additions: against the same fight with one more Defend instead (same shuffle), the second reference line.</summary>
        public Effect? VsDefend { get; init; }

        public const string Header =
            "policy,sample,group,change,kind,card,card_name,type,rarity,cost,contexts,pairs,base_win_pct,changed_win_pct,d_win_pp,d_win_lo,d_win_hi," +
            "wins_gained,wins_lost,base_hp_left,changed_hp_left,d_hp,d_hp_lo,d_hp_hi,base_turns,changed_turns,d_turns," +
            "vs_strike_contexts,vs_strike_d_win_pp,vs_strike_d_win_lo,vs_strike_d_win_hi,vs_strike_d_hp,vs_strike_d_hp_lo,vs_strike_d_hp_hi," +
            "via_strike_d_win_pp,via_strike_d_win_lo,via_strike_d_win_hi,via_strike_d_hp,via_strike_d_hp_lo,via_strike_d_hp_hi," +
            "vs_plain_contexts,vs_plain_d_win_pp,vs_plain_d_win_lo,vs_plain_d_win_hi,vs_plain_d_hp,vs_plain_d_hp_lo,vs_plain_d_hp_hi," +
            "vs_defend_contexts,vs_defend_d_win_pp,vs_defend_d_win_lo,vs_defend_d_win_hi,vs_defend_d_hp,vs_defend_d_hp_lo,vs_defend_d_hp_hi";

        public string ToCsv()
        {
            var d = Direct;
            var cells = new List<string>
            {
                Policy, Sample, Group, Label, Kind, Card, Quote(CardName), Type, Rarity, Cost,
                d.Win.N.ToString(Inv), d.Pairs.ToString(Inv), F(d.BaseWin), F(d.VarWin), F(d.Win.Mean), F(d.Win.Lo), F(d.Win.Hi),
                d.Gained.ToString(Inv), d.Lost.ToString(Inv), F(d.BaseHp), F(d.VarHp), F(d.Hp.Mean), F(d.Hp.Lo), F(d.Hp.Hi),
                F(d.BaseTurns), F(d.VarTurns), F(d.Turns.Mean),
            };
            void Three(Est e) => cells.AddRange([F(e.Mean), F(e.Lo), F(e.Hi)]);
            cells.Add(VsStrike is null ? "" : VsStrike.Win.N.ToString(Inv));
            Three(VsStrike?.Win ?? Est.None);
            Three(VsStrike?.Hp ?? Est.None);
            Three(ViaWin);
            Three(ViaHp);
            cells.Add(VsPlain is null ? "" : VsPlain.Win.N.ToString(Inv));
            Three(VsPlain?.Win ?? Est.None);
            Three(VsPlain?.Hp ?? Est.None);
            cells.Add(VsDefend is null ? "" : VsDefend.Win.N.ToString(Inv));
            Three(VsDefend?.Win ?? Est.None);
            Three(VsDefend?.Hp ?? Est.None);
            return string.Join(',', cells);
        }
    }

    private static string F(double value) => double.IsNaN(value) ? "" : value.ToString("0.00", Inv);

    /// <summary>A number with its sign, "+1.5" or "-0.3"; what rounds to zero is "+0.0".</summary>
    private static string Signed(double value, string format = "0.0")
    {
        var dot = format.IndexOf('.');
        var rounded = Math.Round(value, dot < 0 ? 0 : format.Length - dot - 1);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("+" + format + ";-" + format, Inv);
    }

    private static string Interval(Est e, string format = "0.0") =>
        e.N == 0 || double.IsNaN(e.Mean) ? "-"
        : double.IsNaN(e.Lo) ? Signed(e.Mean, format)
        // Every pair gave the same difference (as a rule: no fight changed at all). That is no measure of how sure the figure is.
        : e.Se == 0 ? $"{Signed(e.Mean, format)} (no pair differed)"
        : $"{Signed(e.Mean, format)} [{Signed(e.Lo, format)}, {Signed(e.Hi, format)}]";

    /// <summary>
    /// The table of a lean run (--only-adds): each addition against one more Strike on the same
    /// fights, for the searching player, with how often the card was played when it could be and
    /// on what kind of turn. Written to adds_table.csv and printed.
    /// </summary>
    private static void SummarizeAdds(string dir, List<Ctx> contexts, List<FightRow> rows, Settings settings, TextWriter output)
    {
        var registry = Content.Registry;
        var results = new Results(rows.Where(r => r.Policy == LookaheadName));
        var inSize = contexts.Where(c => c.Round < settings.Full && TierOrder.Contains(c.Tier)).ToList();
        var ids = FullChanges.Select(c => c.Id).ToList();
        // A fight counts once the Strike and every addition are there, so every card is measured on the same fights.
        var whole = inSize.Where(c => results.Has(c.Id, AddStrikeId) && ids.All(id => results.Has(c.Id, id))).ToList();
        output.WriteLine($"{LookaheadName}: {whole.Count} of {inSize.Count} fights hold the Strike and all {ids.Count} additions ({string.Join(", ", TierOrder.Select(t => $"{whole.Count(c => c.Tier == t)} {t}"))}).");
        var strike = whole.Select(c => results.Get(c.Id, AddStrikeId)).OfType<FightRow>().ToList();
        output.WriteLine(string.Create(Inv, $"With one more Strike: {strike.Count(r => r.Won)} of {strike.Count} won, {(strike.Count > 0 ? strike.Average(r => (double)r.HpLeft) : 0):0.0} HP left (a loss as 0), {(strike.Count > 0 ? strike.Average(r => r.Ms / 1000.0) : 0):0.00} s a fight."));
        output.WriteLine($"Errors: {rows.Count(r => r.Error)}; actions turned down: {rows.Sum(r => r.Refused)}; lost to the turn limit: {rows.Count(r => r.TimedOut)}.");
        output.WriteLine($"Kind of turn: what was coming through the Block already there at the turn's first decision: none 0, small under {LargeDanger}, large {LargeDanger} or more.");
        output.WriteLine();

        var table = new TextTable("card", "fights", "win points", "HP", "elite HP", "boss HP", "fights played in", "played when it could be", "none", "small", "large");
        using var csv = new StreamWriter(Path.Combine(dir, "adds_table.csv"), false, Utf8);
        csv.WriteLine("policy,change,card_name,type,fights,d_win_pp,d_win_lo,d_win_hi,d_hp,d_hp_lo,d_hp_hi,elite_fights,elite_d_hp,elite_d_hp_lo,elite_d_hp_hi,boss_fights,boss_d_hp,boss_d_hp_lo,boss_d_hp_hi," +
                      "strike_win_pct,card_win_pct,strike_hp,card_hp,fights_in_hand,fights_played,hold_none,play_none,hold_small,play_small,hold_large,play_large,first_play_turn");
        var lines = new List<(double Hp, string[] Cells)>();
        foreach (var change in FullChanges)
        {
            var (effect, _, _, _) = Compare(results, whole, [change.Id], AddStrikeId);
            var elite = Compare(results, whole.Where(c => c.Tier == Scenarios.EliteTier), [change.Id], AddStrikeId).Effect;
            var boss = Compare(results, whole.Where(c => c.Tier == Scenarios.BossTier), [change.Id], AddStrikeId).Effect;
            var played = whole.Select(c => results.Get(c.Id, change.Id)).OfType<FightRow>().ToList();
            int holdNone = played.Sum(r => r.HoldNone), holdSmall = played.Sum(r => r.HoldSmall), holdLarge = played.Sum(r => r.HoldLarge);
            int playNone = played.Sum(r => r.PlayNone), playSmall = played.Sum(r => r.PlaySmall), playLarge = played.Sum(r => r.PlayLarge);
            static string Share(int played, int held) => held == 0 ? "-" : string.Create(Inv, $"{100.0 * played / held:0}% of {held}");
            var def = registry.Card(change.Card);
            var name = def.Name + (change.Kind == "addup" ? "+" : "");
            var firsts = played.Where(r => r.FirstPlayTurn > 0).Select(r => (double)r.FirstPlayTurn).ToList();
            lines.Add((effect.Hp.Mean, [
                name, effect.Win.N.ToString(Inv), Interval(effect.Win), Interval(effect.Hp), Interval(elite.Hp), Interval(boss.Hp),
                $"{played.Count(r => r.Plays > 0)} of {played.Count(r => r.HandTurns > 0)}",
                Share(playNone + playSmall + playLarge, holdNone + holdSmall + holdLarge), Share(playNone, holdNone), Share(playSmall, holdSmall), Share(playLarge, holdLarge)]));
            csv.WriteLine(string.Join(',', LookaheadName, change.Id, Quote(name), def.Type.ToString(), effect.Win.N.ToString(Inv),
                F(effect.Win.Mean), F(effect.Win.Lo), F(effect.Win.Hi), F(effect.Hp.Mean), F(effect.Hp.Lo), F(effect.Hp.Hi),
                elite.Hp.N.ToString(Inv), F(elite.Hp.Mean), F(elite.Hp.Lo), F(elite.Hp.Hi), boss.Hp.N.ToString(Inv), F(boss.Hp.Mean), F(boss.Hp.Lo), F(boss.Hp.Hi),
                F(effect.BaseWin), F(effect.VarWin), F(effect.BaseHp), F(effect.VarHp),
                played.Count(r => r.HandTurns > 0).ToString(Inv), played.Count(r => r.Plays > 0).ToString(Inv),
                holdNone.ToString(Inv), playNone.ToString(Inv), holdSmall.ToString(Inv), playSmall.ToString(Inv), holdLarge.ToString(Inv), playLarge.ToString(Inv),
                firsts.Count > 0 ? F(firsts.Average()) : ""));
        }
        foreach (var line in lines.OrderByDescending(l => l.Hp)) table.Add(line.Cells);
        output.WriteLine($"Each card against one more Strike in its place, same fight and shuffle, played by {LookaheadName}. Win points and HP left (a loss as 0), with 95% intervals.");
        table.Write(output);
        output.WriteLine($"Table: {Path.Combine(dir, "adds_table.csv")}");
    }

    private static void Summarize(string dir, List<Ctx> contexts, List<FightRow> rows, Settings settings, TextWriter output)
    {
        var registry = Content.Registry;
        var byPolicy = new Dictionary<string, Results>(StringComparer.Ordinal)
        {
            [LookaheadName] = new(rows.Where(r => r.Policy == LookaheadName)),
            [GreedyName] = new(rows.Where(r => r.Policy == GreedyName)),
        };
        var light = contexts.ToDictionary(c => c.Id, LightChanges, StringComparer.Ordinal);
        var fullIds = FullChanges.Select(c => c.Id).ToList();

        // A context counts for a policy once every fight planned for it is there: so every change is measured on the same contexts.
        HashSet<string> LightDone(Results r) => contexts.Where(c => light[c.Id].All(ch => r.Has(c.Id, ch.Id))).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> FullDone(Results r, HashSet<string> lightDone) =>
            contexts.Where(c => lightDone.Contains(c.Id) && fullIds.All(id => r.Has(c.Id, id))).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        // Lookahead's additions count on the rounds the run was sized for, so that every tier has the same number
        // of fights and every enemy of a tier its even share; a round a stopped run left half done is not used.
        var inSize = contexts.Where(c => c.Round < settings.Full).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var lookLight = LightDone(byPolicy[LookaheadName]);
        var lookFull = FullDone(byPolicy[LookaheadName], lookLight);
        lookFull.IntersectWith(inSize);
        var greedyLight = LightDone(byPolicy[GreedyName]);
        var greedyFull = FullDone(byPolicy[GreedyName], greedyLight);

        // (policy, sample name, contexts for light changes, contexts for additions)
        var samples = new List<(string Policy, string Sample, HashSet<string> Light, HashSet<string> Full)>
        {
            (LookaheadName, "paired", lookLight, lookFull),
            (GreedyName, "same fights as Lookahead", greedyLight.Intersect(lookLight).ToHashSet(StringComparer.Ordinal), greedyFull.Intersect(lookFull).ToHashSet(StringComparer.Ordinal)),
        };
        if (greedyFull.Count > lookFull.Count) samples.Add((GreedyName, "all fights", greedyLight, greedyFull));

        // Groups of fights: each tier, elites and bosses together, and each elite and boss enemy.
        var groups = new List<(string Name, bool ByEnemy, Func<Ctx, bool> In)>();
        foreach (var tier in TierOrder) groups.Add((tier, false, c => c.Tier == tier));
        groups.Add(("elite+boss", false, c => c.Tier is Scenarios.EliteTier or Scenarios.BossTier));
        foreach (var encounter in contexts.Where(c => c.Tier != Scenarios.NormalTier).Select(c => c.Setup.Encounter).Distinct().Order(StringComparer.Ordinal))
            groups.Add((encounter, true, c => c.Setup.Encounter == encounter));

        // The changes, and named groups of changes ("any reward card", "any Power").
        var pool = registry.IroncladPool;
        var rewardCards = pool.Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare).ToList();
        var labels = new List<(string Label, string Kind, string Card, List<string> Changes, bool Additions)>();
        // One more Strike is the reference: it is measured on every fight. One more Defend is ranked with the other
        // additions, so it is measured on the fights they have; the "basic" lines keep both on every fight.
        foreach (var def in pool) labels.Add(("add:" + def.ClassName, "add", def.ClassName, ["add:" + def.ClassName], def.ClassName != Strike));
        labels.Add((AddStrikeId, "basic", Strike, [AddStrikeId], false));
        labels.Add((AddDefendId, "basic", Defend, [AddDefendId], false));
        foreach (var def in pool.Where(d => d.MaxUpgradeLevel > 0)) labels.Add(("add:" + def.ClassName + "+", "addup", def.ClassName, ["add:" + def.ClassName + "+"], true));
        labels.Add(("remove:" + Strike, "remove", Strike, ["remove:" + Strike], false));
        labels.Add(("remove:" + Defend, "remove", Defend, ["remove:" + Defend], false));
        foreach (var def in pool.Where(d => d.MaxUpgradeLevel > 0)) labels.Add(("upgrade:" + def.ClassName, "upgrade", def.ClassName, ["upgrade:" + def.ClassName], false));
        void Named(string label, string kind, IEnumerable<CardDef> cards, string prefix, string suffix, bool additions) =>
            labels.Add((label, kind, "", cards.Select(c => prefix + c.ClassName + suffix).ToList(), additions));
        Named("add:*any reward card", "add-group", rewardCards, "add:", "", true);
        foreach (var type in new[] { CardType.Attack, CardType.Skill, CardType.Power })
            Named($"add:*any {type}", "add-group", rewardCards.Where(c => c.Type == type), "add:", "", true);
        foreach (var rarity in new[] { CardRarity.Common, CardRarity.Uncommon, CardRarity.Rare })
            Named($"add:*any {rarity}", "add-group", rewardCards.Where(c => c.Rarity == rarity), "add:", "", true);
        Named("add:*any reward card+", "addup-group", rewardCards, "add:", "+", true);
        foreach (var type in new[] { CardType.Attack, CardType.Skill, CardType.Power })
            Named($"add:*any {type}+", "addup-group", rewardCards.Where(c => c.Type == type), "add:", "+", true);
        Named("upgrade:*any card but Strike and Defend", "upgrade-group", pool.Where(c => c.ClassName is not (Strike or Defend)), "upgrade:", "", false);
        Named("upgrade:*any card but Strike, Defend and Bash", "upgrade-group", pool.Where(c => c.Rarity != CardRarity.Basic), "upgrade:", "", false);

        var lines = new List<Line>();
        foreach (var (policy, sample, lightSet, fullSet) in samples)
        {
            var results = byPolicy[policy];
            foreach (var (groupName, byEnemy, inGroup) in groups)
            {
                var groupLight = contexts.Where(c => inGroup(c) && lightSet.Contains(c.Id)).ToList();
                var groupFull = contexts.Where(c => inGroup(c) && fullSet.Contains(c.Id)).ToList();

                // One more Strike against the fight as it is, on every context: the second half of the "via Strike" figure.
                var strikeAll = Compare(results, groupLight, [AddStrikeId]);
                var strikeByContext = strikeAll.Contexts.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);

                foreach (var (label, kind, card, changes, additions) in labels)
                {
                    var set = additions ? groupFull : groupLight;
                    var direct = Compare(results, set, changes);
                    if (direct.Effect.Win.N == 0 || (byEnemy && direct.Effect.Win.N < MinEnemyPairs)) continue;
                    var def = card.Length > 0 ? registry.Card(card) : null;
                    var line = new Line
                    {
                        Policy = policy, Sample = sample, Group = groupName, Label = label, Kind = kind, Card = card,
                        CardName = def is null ? label[(label.IndexOf('*') + 1)..] : def.Name + (kind == "addup" ? "+" : ""),
                        Type = def?.Type.ToString() ?? "", Rarity = def?.Rarity.ToString() ?? "",
                        Cost = def is null ? "" : def.CostsX ? "X" : def.Cost.ToString(Inv),
                        Direct = direct.Effect,
                    };
                    if (kind.StartsWith("add", StringComparison.Ordinal) && label != AddStrikeId)
                    {
                        // The same fights with one more Strike instead: the tight pair. Then add what the Strike itself is worth.
                        var vs = Compare(results, groupFull.Count > 0 && additions ? groupFull : set, changes, AddStrikeId);
                        if (vs.Effect.Win.N >= 2 && vs.Contexts.All(strikeByContext.ContainsKey))
                        {
                            var onSameWin = vs.Contexts.Select(id => strikeAll.Win[strikeByContext[id]]).ToList();
                            var onSameHp = vs.Contexts.Select(id => strikeAll.Hp[strikeByContext[id]]).ToList();
                            line = line with
                            {
                                VsStrike = vs.Effect,
                                ViaWin = Est.Sum(vs.Win, onSameWin, strikeAll.Win),
                                ViaHp = Est.Sum(vs.Hp, onSameHp, strikeAll.Hp),
                            };
                        }
                    }
                    if (kind.StartsWith("add", StringComparison.Ordinal) && label != AddStrikeId && label != AddDefendId)
                    {
                        var vsDefend = Compare(results, groupFull.Count > 0 && additions ? groupFull : set, changes, AddDefendId);
                        if (vsDefend.Effect.Win.N >= 2) line = line with { VsDefend = vsDefend.Effect };
                    }
                    if (kind is "addup" or "addup-group")
                    {
                        // Per context the upgraded additions against the same cards added plain.
                        var plain = changes.Select(c => c[..^1]).ToList();
                        line = line with { VsPlain = ComparePlain(results, set, changes, plain) };
                    }
                    lines.Add(line);
                }
            }
        }

        // ---- files ----
        using (var w = new StreamWriter(Path.Combine(dir, "summary_changes.csv"), false, Utf8))
        {
            w.WriteLine(Line.Header);
            foreach (var line in lines.Where(l => !groups.First(g => g.Name == l.Group).ByEnemy)) w.WriteLine(line.ToCsv());
        }
        using (var w = new StreamWriter(Path.Combine(dir, "summary_by_enemy.csv"), false, Utf8))
        {
            w.WriteLine(Line.Header);
            foreach (var line in lines.Where(l => groups.First(g => g.Name == l.Group).ByEnemy)) w.WriteLine(line.ToCsv());
        }
        WritePairs(dir, contexts, byPolicy, lookLight, lookFull);
        var playRates = WritePlayRates(dir, contexts, byPolicy, lookFull, greedyFull);
        WriteValuesTable(dir, lines, playRates);
        WriteBaseline(dir, contexts, byPolicy, lookLight, greedyLight);

        // ---- the report ----
        using var report = new StringWriter();
        WriteReport(report, contexts, byPolicy, lines, playRates, settings, lookLight, lookFull, greedyLight, greedyFull, rows);
        File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString());
        output.Write(report.ToString());
        output.WriteLine();
        output.WriteLine($"Tables: {dir}/report.txt, values_table.csv (every addition against one more Strike, with the cards not to trust marked), summary_changes.csv, summary_by_enemy.csv, play_rates.csv, baseline.csv, pairs.csv, contexts.csv, fights.*.csv (the log, one per worker), run.json");
    }

    /// <summary>Upgraded additions against the same cards added plain, context by context.</summary>
    private static Effect ComparePlain(Results results, IEnumerable<Ctx> contexts, IReadOnlyList<string> upgraded, IReadOnlyList<string> plain)
    {
        List<double> dWin = new(), dHp = new(), dTurns = new();
        var pairs = 0;
        int gained = 0, lost = 0;
        double baseWin = 0, varWin = 0, baseHp = 0, varHp = 0;
        foreach (var ctx in contexts)
        {
            double win = 0, hp = 0, turns = 0, bw = 0, bh = 0;
            var n = 0;
            for (var i = 0; i < upgraded.Count; i++)
            {
                if (results.Get(ctx.Id, upgraded[i]) is not { } u || results.Get(ctx.Id, plain[i]) is not { } p) continue;
                n++;
                win += (u.Won ? 1 : 0) - (p.Won ? 1 : 0);
                hp += u.HpLeft - p.HpLeft;
                turns += u.Turns - p.Turns;
                bw += p.Won ? 1 : 0;
                bh += p.HpLeft;
                if (u.Won && !p.Won) gained++;
                if (!u.Won && p.Won) lost++;
            }
            if (n == 0) continue;
            pairs += n;
            dWin.Add(100 * win / n);
            dHp.Add(hp / n);
            dTurns.Add(turns / n);
            baseWin += bw / n;
            baseHp += bh / n;
            varWin += (bw + win) / n;
            varHp += (bh + hp) / n;
        }
        var c = Math.Max(1, dWin.Count);
        return new Effect
        {
            Pairs = pairs, Gained = gained, Lost = lost, BaseWin = 100 * baseWin / c, VarWin = 100 * varWin / c, BaseHp = baseHp / c, VarHp = varHp / c,
            Win = Est.Of(dWin), Hp = Est.Of(dHp), Turns = Est.Of(dTurns),
        };
    }

    /// <summary>pairs.csv: one row per pair, the fight as it is next to the changed fight.</summary>
    private static void WritePairs(string dir, List<Ctx> contexts, Dictionary<string, Results> byPolicy, HashSet<string> lookLight, HashSet<string> lookFull)
    {
        using var w = new StreamWriter(Path.Combine(dir, "pairs.csv"), false, Utf8);
        w.WriteLine("policy,context,tier,encounter,seed,in_lookahead_sample,change,kind,card,base_won,changed_won,d_won,base_hp_left,changed_hp_left,d_hp_left," +
                    "base_turns,changed_turns,base_enemy_hp_left,changed_enemy_hp_left,x_hand_turns,x_play_turns,x_plays,x_first_hand_turn,x_first_play_turn");
        foreach (var (policy, results) in byPolicy)
            foreach (var ctx in contexts)
            {
                if (results.Get(ctx.Id, BaseId) is not { } b) continue;
                foreach (var change in LightChanges(ctx).Concat(FullChanges))
                {
                    if (change.Kind == "base" || results.Get(ctx.Id, change.Id) is not { } v) continue;
                    var isLight = change.Kind is "remove" or "upgrade" or "reorder" || change.Id == AddStrikeId || change.Id == "add:" + Defend;
                    var inSample = (isLight ? lookLight : lookFull).Contains(ctx.Id);
                    w.WriteLine(string.Create(Inv,
                        $"{policy},{ctx.Id},{ctx.Tier},{ctx.Setup.Encounter},{ctx.Scenario.Seed},{(inSample ? 1 : 0)},{change.Id},{change.Kind},{change.Card}," +
                        $"{(b.Won ? 1 : 0)},{(v.Won ? 1 : 0)},{(v.Won ? 1 : 0) - (b.Won ? 1 : 0)},{b.HpLeft},{v.HpLeft},{v.HpLeft - b.HpLeft},{b.Turns},{v.Turns}," +
                        $"{b.EnemyHpLeft},{v.EnemyHpLeft},{v.HandTurns},{v.PlayTurns},{v.Plays},{v.FirstHandTurn},{v.FirstPlayTurn}"));
                }
            }
    }

    /// <summary>How a player treated an added card: of the fights and turns it had the card in hand, in how many it played it.</summary>
    private sealed record PlayRate(string Policy, string Group, string Card, bool Upgraded, int Fights, int FightsInHand, int FightsPlayed,
        int HandTurns, int PlayTurns, double MeanFirstHandTurn, double MeanFirstPlayTurn, double MeanTurns);

    private static List<PlayRate> WritePlayRates(string dir, List<Ctx> contexts, Dictionary<string, Results> byPolicy, HashSet<string> lookFull, HashSet<string> greedyFull)
    {
        var rates = new List<PlayRate>();
        var groups = new (string Name, Func<Ctx, bool> In)[]
        {
            ("normal", c => c.Tier == Scenarios.NormalTier), ("elite", c => c.Tier == Scenarios.EliteTier), ("boss", c => c.Tier == Scenarios.BossTier),
            ("elite+boss", c => c.Tier != Scenarios.NormalTier), ("all", _ => true),
        };
        foreach (var (policy, set) in new[] { (LookaheadName, lookFull), (GreedyName, greedyFull) })
            foreach (var (groupName, inGroup) in groups)
                foreach (var def in Content.Registry.IroncladPool)
                    foreach (var upgraded in new[] { false, true })
                    {
                        var id = "add:" + def.ClassName + (upgraded ? "+" : "");
                        // Strike and Defend are played on every context; keep to the contexts the other cards have.
                        var fights = contexts.Where(c => inGroup(c) && set.Contains(c.Id)).Select(c => byPolicy[policy].Get(c.Id, id)).OfType<FightRow>().ToList();
                        if (fights.Count == 0) continue;
                        var inHand = fights.Where(f => f.HandTurns > 0).ToList();
                        var played = fights.Where(f => f.Plays > 0).ToList();
                        rates.Add(new PlayRate(policy, groupName, def.ClassName, upgraded, fights.Count, inHand.Count, played.Count,
                            fights.Sum(f => f.HandTurns), fights.Sum(f => f.PlayTurns),
                            inHand.Count > 0 ? inHand.Average(f => (double)f.FirstHandTurn) : double.NaN,
                            played.Count > 0 ? played.Average(f => (double)f.FirstPlayTurn) : double.NaN,
                            fights.Average(f => (double)f.Turns)));
                    }

        using var w = new StreamWriter(Path.Combine(dir, "play_rates.csv"), false, Utf8);
        w.WriteLine("policy,group,card,card_name,type,cost,upgraded,expected_undervalued,fights,fights_card_in_hand,fights_card_played,pct_fights_played_of_in_hand," +
                    "turns_in_hand,turns_played,pct_turns_played,mean_first_turn_in_hand,mean_first_turn_played,mean_fight_turns");
        foreach (var r in rates)
        {
            var def = Content.Registry.Card(r.Card);
            w.WriteLine(string.Join(',', r.Policy, r.Group, r.Card, Quote(def.Name), def.Type, def.CostsX ? "X" : def.Cost.ToString(Inv), r.Upgraded ? "1" : "0",
                ExpectedUndervalued.Contains(r.Card) ? "1" : "0", r.Fights.ToString(Inv), r.FightsInHand.ToString(Inv), r.FightsPlayed.ToString(Inv),
                F(r.FightsInHand > 0 ? 100.0 * r.FightsPlayed / r.FightsInHand : double.NaN), r.HandTurns.ToString(Inv), r.PlayTurns.ToString(Inv),
                F(r.HandTurns > 0 ? 100.0 * r.PlayTurns / r.HandTurns : double.NaN), F(r.MeanFirstHandTurn), F(r.MeanFirstPlayTurn), F(r.MeanTurns)));
        }
        return rates;
    }

    /// <summary>A card is called rarely played below this share of the turns it was in hand.</summary>
    private const double RarelyPlayed = 0.30;

    /// <summary>A figure that rests on fewer pairs than this is marked.</summary>
    private const int FewPairs = 100;

    /// <summary>The play rate that goes with a line: the same player and group of fights (Greedy's is counted on all its fights).</summary>
    private static PlayRate? RateOf(List<PlayRate> rates, Line l) =>
        rates.FirstOrDefault(r => r.Policy == l.Policy && r.Group == l.Group && r.Card == l.Card && r.Upgraded == (l.Kind == "addup"));

    /// <summary>Why an addition's figure should not be read as the card's worth; empty when nothing speaks against it.</summary>
    private static List<string> Doubts(Line l, PlayRate? rate)
    {
        var doubts = new List<string>();
        if (rate is { HandTurns: > 0 } r && (double)r.PlayTurns / r.HandTurns < RarelyPlayed)
            doubts.Add(string.Create(Inv, $"{l.Policy} played it in {100.0 * r.PlayTurns / r.HandTurns:0}% of the turns it was in hand ({r.FightsPlayed} of {r.FightsInHand} fights): the figure is mostly that of a card left unplayed"));
        if (ExpectedUndervalued.Contains(l.Card))
            doubts.Add("a Power that pays over several turns: a player that looks one turn ahead underrates it");
        if (l.Direct.Win.N < FewPairs)
            doubts.Add(string.Create(Inv, $"only {l.Direct.Win.N} pairs"));
        return doubts;
    }

    /// <summary>"better", "worse" or "not told apart": where the 95% interval of the HP difference lies.</summary>
    private static string Against(Est? hp) =>
        hp is not { } e || e.N < 2 || double.IsNaN(e.Lo) ? "" : e.Lo > 0 ? "better" : e.Hi < 0 ? "worse" : "not told apart";

    /// <summary>values_table.csv: every addition against the two reference lines, one more Strike and one more Defend,
    /// ranked within its player and group of fights, with its play rate and what speaks against trusting the figure.</summary>
    private static void WriteValuesTable(string dir, List<Line> lines, List<PlayRate> rates)
    {
        using var w = new StreamWriter(Path.Combine(dir, "values_table.csv"), false, Utf8);
        w.WriteLine("policy,sample,group,kind,rank_by_hp_vs_strike,change,card_name,type,rarity,cost,fights," +
                    "hp_vs_strike,hp_vs_strike_lo,hp_vs_strike_hi,against_strike,win_pp_vs_strike,win_pp_vs_strike_lo,win_pp_vs_strike_hi," +
                    "hp_vs_defend,hp_vs_defend_lo,hp_vs_defend_hi,against_defend," +
                    "hp_vs_fight_as_is,hp_vs_fight_as_is_lo,hp_vs_fight_as_is_hi,win_pp_vs_fight_as_is,win_pp_vs_fight_as_is_lo,win_pp_vs_fight_as_is_hi," +
                    "pct_turns_played_of_in_hand,pct_fights_played_of_in_hand,trust,why_not");
        foreach (var block in lines.Where(l => l.Kind is "add" or "addup").GroupBy(l => (l.Policy, l.Sample, l.Group, l.Kind)))
        {
            // One more Strike is 0 on its own scale.
            static double VsStrikeHp(Line l) => l.Label == AddStrikeId ? 0 : l.VsStrike?.Hp.Mean ?? double.NaN;
            var rank = 0;
            foreach (var l in block.Where(l => !double.IsNaN(VsStrikeHp(l))).OrderByDescending(VsStrikeHp))
            {
                rank++;
                var rate = RateOf(rates, l);
                var doubts = Doubts(l, rate);
                var isStrike = l.Label == AddStrikeId;
                var via = isStrike ? l.Direct.Hp : l.ViaHp;
                var viaWin = isStrike ? l.Direct.Win : l.ViaWin;
                var cells = new List<string>
                {
                    l.Policy, l.Sample, l.Group, l.Kind, rank.ToString(Inv), l.Label, Quote(l.CardName), l.Type, l.Rarity, l.Cost, l.Direct.Win.N.ToString(Inv),
                };
                void Three(Est? e) => cells.AddRange(e is { } x ? [F(x.Mean), F(x.Lo), F(x.Hi)] : ["", "", ""]);
                if (isStrike) cells.AddRange(["0.00", "", "", "the reference", "0.00", "", ""]);
                else
                {
                    Three(l.VsStrike?.Hp);
                    cells.Add(Against(l.VsStrike?.Hp));
                    Three(l.VsStrike?.Win);
                }
                if (l.Label == AddDefendId) cells.AddRange(["0.00", "", "", "the reference"]);
                else
                {
                    Three(l.VsDefend?.Hp);
                    cells.Add(Against(l.VsDefend?.Hp));
                }
                Three(via);
                Three(viaWin);
                cells.Add(rate is { HandTurns: > 0 } ? F(100.0 * rate.PlayTurns / rate.HandTurns) : "");
                cells.Add(rate is { FightsInHand: > 0 } ? F(100.0 * rate.FightsPlayed / rate.FightsInHand) : "");
                cells.Add(doubts.Count == 0 ? "ok" : "doubtful");
                cells.Add(Quote(string.Join("; ", doubts)));
                w.WriteLine(string.Join(',', cells));
            }
        }
    }

    /// <summary>baseline.csv: the fights as they are, by tier and by enemy.</summary>
    private static void WriteBaseline(string dir, List<Ctx> contexts, Dictionary<string, Results> byPolicy, HashSet<string> lookLight, HashSet<string> greedyLight)
    {
        using var w = new StreamWriter(Path.Combine(dir, "baseline.csv"), false, Utf8);
        w.WriteLine("policy,tier,encounter,fights,win_pct,win_lo,win_hi,mean_hp_left,mean_hp_left_on_wins,mean_turns,mean_hp_start,mean_deck_size");
        foreach (var (policy, set) in new[] { (LookaheadName, lookLight), (GreedyName, greedyLight) })
        {
            void Write(string tier, string encounter, List<Ctx> list)
            {
                var fights = list.Where(c => set.Contains(c.Id)).Select(c => (Ctx: c, Row: byPolicy[policy].Get(c.Id, BaseId))).Where(x => x.Row is not null).ToList();
                if (fights.Count == 0) return;
                var wins = fights.Count(f => f.Row!.Won);
                var (lo, hi) = Stats.Wilson(wins, fights.Count);
                w.WriteLine(string.Join(',', policy, tier, encounter, fights.Count.ToString(Inv), F(100.0 * wins / fights.Count), F(100 * lo), F(100 * hi),
                    F(fights.Average(f => (double)f.Row!.HpLeft)), F(wins > 0 ? fights.Where(f => f.Row!.Won).Average(f => (double)f.Row!.HpLeft) : double.NaN),
                    F(fights.Average(f => (double)f.Row!.Turns)), F(fights.Average(f => (double)f.Row!.HpStart)), F(fights.Average(f => (double)f.Ctx.Setup.Deck.Count))));
            }
            foreach (var tier in TierOrder)
            {
                var inTier = contexts.Where(c => c.Tier == tier).ToList();
                Write(tier, "(all)", inTier);
                foreach (var encounter in inTier.Select(c => c.Setup.Encounter).Distinct().Order(StringComparer.Ordinal))
                    Write(tier, encounter, inTier.Where(c => c.Setup.Encounter == encounter).ToList());
            }
        }
    }

    // =====================================================================================
    // The report
    // =====================================================================================

    /// <summary>The two reference lines (one more Strike, one more Defend), where the other cards stand against them, and the figures not to trust.</summary>
    private static void WriteReference(TextWriter o, List<Line> lines, List<PlayRate> rates)
    {
        o.WriteLine("THE REFERENCE LINES: ONE MORE STRIKE AND ONE MORE DEFEND (read a card against these, not against zero)");
        var table = new TextTable("player", "fights of", "tier", "+Strike vs as it is: win", "HP", "+Defend vs as it is: win", "HP", "+Defend vs +Strike: win", "HP", "average reward card vs +Strike: win", "HP");
        foreach (var (policy, sample) in new[] { (LookaheadName, "paired"), (GreedyName, "same fights as Lookahead"), (GreedyName, "all fights") })
            foreach (var group in new[] { "normal", "elite", "boss", "elite+boss" })
            {
                Line? One(string label, string kind) => lines.FirstOrDefault(l => l.Policy == policy && l.Sample == sample && l.Group == group && l.Label == label && l.Kind == kind);
                var strike = One(AddStrikeId, "basic");
                var defend = One(AddDefendId, "basic");
                var defendSame = One(AddDefendId, "add");
                var any = One("add:*any reward card", "add-group");
                if (strike is null || defend is null) continue;
                table.Add(policy, sample == "paired" ? "all, additions on a part" : sample, group, Interval(strike.Direct.Win), Interval(strike.Direct.Hp), Interval(defend.Direct.Win), Interval(defend.Direct.Hp),
                    defendSame?.VsStrike is { } d ? Interval(d.Win) : "-", defendSame?.VsStrike is { } d2 ? Interval(d2.Hp) : "-",
                    any?.VsStrike is { } a ? Interval(a.Win) : "-", any?.VsStrike is { } a2 ? Interval(a2.Hp) : "-");
            }
        table.Write(o);
        o.WriteLine("The last two columns are the mean over all reward cards, fight by fight: their interval is how far the luck of the plus-one-Strike fights can move every card's figure at once.");
        o.WriteLine();

        foreach (var (policy, sample) in new[] { (LookaheadName, "paired"), (GreedyName, "all fights"), (GreedyName, "same fights as Lookahead") })
        {
            var adds = lines.Where(l => l.Policy == policy && l.Sample == sample && l.Group == "elite+boss" && l.Kind == "add" && l.VsStrike is not null).ToList();
            if (adds.Count == 0) continue;
            if (policy == GreedyName && sample != "all fights" && lines.Any(l => l.Policy == GreedyName && l.Sample == "all fights")) continue;
            string Names(IEnumerable<Line> list) => string.Join(", ", list.Select(l => $"{l.CardName} {Signed(l.VsStrike!.Hp.Mean)}"));
            var better = adds.Where(l => Against(l.VsStrike!.Hp) == "better").OrderByDescending(l => l.VsStrike!.Hp.Mean).ToList();
            var worse = adds.Where(l => Against(l.VsStrike!.Hp) == "worse").OrderBy(l => l.VsStrike!.Hp.Mean).ToList();
            var same = adds.Where(l => Against(l.VsStrike!.Hp) == "not told apart").OrderByDescending(l => l.VsStrike!.Hp.Mean).ToList();
            o.WriteLine($"{policy} ({sample}), elites and bosses, HP against one more Strike, {adds.Count} cards added plain:");
            o.WriteLine($"  better than one more Strike beyond the interval: {better.Count}");
            o.WriteLine($"  not told apart from one more Strike ({same.Count}): {Names(same)}");
            o.WriteLine($"  worse than one more Strike beyond the interval ({worse.Count}): {Names(worse)}");
            var belowDefend = adds.Where(l => l.VsDefend is not null && Against(l.VsDefend.Hp) == "worse").OrderBy(l => l.VsDefend!.Hp.Mean).ToList();
            o.WriteLine($"  worse than one more Defend beyond the interval ({belowDefend.Count}): {string.Join(", ", belowDefend.Select(l => $"{l.CardName} {Signed(l.VsDefend!.Hp.Mean)}"))}");
        }
        o.WriteLine();

        o.WriteLine("NUMBERS NOT TO TRUST (elites and bosses, the card added plain)");
        o.WriteLine(string.Create(Inv, $"A card the player plays in under {100 * RarelyPlayed:0}% of the turns it holds it is measured as a card left in the hand, not as the card. The Powers named"));
        o.WriteLine("before the run pay over several turns, which neither player counts. values_table.csv marks every such row \"doubtful\" with the reason.");
        foreach (var policy in new[] { LookaheadName, GreedyName })
        {
            var sample = policy == LookaheadName ? "paired" : lines.Any(l => l.Policy == GreedyName && l.Sample == "all fights") ? "all fights" : "same fights as Lookahead";
            var adds = lines.Where(l => l.Policy == policy && l.Sample == sample && l.Group == "elite+boss" && l.Kind == "add").ToList();
            var rare = adds.Select(l => (Line: l, Rate: RateOf(rates, l))).Where(x => x.Rate is { HandTurns: > 0 } r && (double)r.PlayTurns / r.HandTurns < RarelyPlayed)
                .OrderBy(x => (double)x.Rate!.PlayTurns / x.Rate.HandTurns).ToList();
            o.WriteLine($"{policy}: rarely played ({rare.Count}): " + string.Join(", ", rare.Select(x => string.Create(Inv, $"{x.Line.CardName} {100.0 * x.Rate!.PlayTurns / x.Rate.HandTurns:0}%"))));
            var never = rare.Where(x => x.Rate!.PlayTurns == 0).Select(x => x.Line).ToList();
            if (never.Count > 0)
            {
                var level = never.Where(l => l.VsStrike is not null).Select(l => l.VsStrike!.Hp.Mean).ToList();
                if (level.Count > 0)
                    o.WriteLine(string.Create(Inv, $"  {never.Count} of them it never played at all; they stand at {Signed(level.Average())} HP against one more Strike on average: what a card that is never played is worth to this player."));
            }
            else if (rare.Count > 0 && rare[0].Line.VsStrike is { } least)
                o.WriteLine(string.Create(Inv, $"  The one it plays least, {rare[0].Line.CardName}, stands at {Signed(least.Hp.Mean)} HP against one more Strike: about what a card that is never played is worth to this player."));
            var powers = adds.Where(l => ExpectedUndervalued.Contains(l.Card) && rare.All(x => x.Line != l)).OrderBy(l => l.CardName, StringComparer.Ordinal).ToList();
            if (powers.Count > 0) o.WriteLine($"  Powers named before the run as paying over several turns, which it plays but does not plan around (read the figure as a floor): {string.Join(", ", powers.Select(l => l.CardName))}");
        }
        o.WriteLine(string.Create(Inv, $"Fewer than {FewPairs} pairs: every line of ONE CARD OF THE DECK UPGRADED marked with *, and every line of BY ENEMY (see the fights in brackets there)."));
        o.WriteLine();
    }

    /// <summary>The same deck in another order: a change that changes nothing but the draw. Its mean must be 0; its spread is the noise of the deal.</summary>
    private static void WriteSameDeck(TextWriter o, List<Ctx> contexts, Dictionary<string, Results> byPolicy, Settings settings)
    {
        if (settings.Aa <= 0) return;
        o.WriteLine("THE SAME DECK IN ANOTHER ORDER (nothing changes but the draw: the true difference is 0, the spread is the noise a pair with another shuffle carries)");
        var table = new TextTable("player", "tier", "fights", "win", "HP", "SD of one pair's HP difference", "+Strike vs as it is, SD", "pairs", "pairs with the same HP left");
        foreach (var policy in new[] { LookaheadName, GreedyName })
            foreach (var group in new[] { "normal", "elite", "boss", "elite+boss" })
            {
                bool In(Ctx c) => group == "elite+boss" ? c.Tier != Scenarios.NormalTier : c.Tier == group;
                // win and hp: one value per fight (the mean over its other orders, minus the fight as it is), so the interval is honest.
                List<double> win = new(), hp = new(), strike = new(), pairs = new();
                var same = 0;
                foreach (var ctx in contexts.Where(In))
                {
                    if (byPolicy[policy].Get(ctx.Id, BaseId) is not { } b) continue;
                    if (byPolicy[policy].Get(ctx.Id, AddStrikeId) is { } s) strike.Add(s.HpLeft - b.HpLeft);
                    double w = 0, h = 0;
                    var n = 0;
                    for (var k = 1; k <= settings.Aa; k++)
                    {
                        if (byPolicy[policy].Get(ctx.Id, Change.Reorder(k).Id) is not { } r) continue;
                        n++;
                        w += 100.0 * ((r.Won ? 1 : 0) - (b.Won ? 1 : 0));
                        h += r.HpLeft - b.HpLeft;
                        pairs.Add(r.HpLeft - b.HpLeft);
                        if (r.HpLeft == b.HpLeft) same++;
                    }
                    if (n == 0) continue;
                    win.Add(w / n);
                    hp.Add(h / n);
                }
                if (hp.Count < 2) continue;
                static double Sd(List<double> v)
                {
                    if (v.Count < 2) return double.NaN;
                    var m = v.Average();
                    return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1));
                }
                table.Add(policy, group, hp.Count.ToString(Inv), Interval(Est.Of(win)), Interval(Est.Of(hp)), Sd(pairs).ToString("0.0", Inv), Sd(strike).ToString("0.0", Inv), pairs.Count.ToString(Inv), same.ToString(Inv));
            }
        table.Write(o);
        o.WriteLine("win and HP: per fight, the mean over its other orders minus the fight as it is. An interval that holds 0 is what an honest interval should give here.");
        o.WriteLine();
    }

    private static void WriteReport(TextWriter o, List<Ctx> contexts, Dictionary<string, Results> byPolicy, List<Line> lines, List<PlayRate> playRates,
        Settings settings, HashSet<string> lookLight, HashSet<string> lookFull, HashSet<string> greedyLight, HashSet<string> greedyFull, List<FightRow> rows)
    {
        var registry = Content.Registry;
        o.WriteLine("CARD VALUES, MEASURED IN THE ARENA (v0.107.1, Ironclad, act 1 Overgrowth)");
        o.WriteLine(string.Create(Inv, $"Seed {settings.Seed}; {settings.Contexts} fights per tier (normal, elite, boss) from the arena's generator; every addition on {settings.Full} of each tier, taken evenly from the tier's enemies"));
        o.WriteLine(string.Create(Inv, $"(asked for {settings.FullAsked}); Lookahead with budget {settings.LookaheadBudget} and {settings.LookaheadWorlds} worlds; turn limit {settings.MaxTurns}."));
        var look = rows.Count(r => r.Policy == LookaheadName);
        var greedy = rows.Count(r => r.Policy == GreedyName);
        var errors = rows.Count(r => r.Error);
        var refused = rows.Count(r => r.Refused > 0);
        var timedOut = $"{rows.Count(r => r.TimedOut && r.Policy == LookaheadName)} by Lookahead, {rows.Count(r => r.TimedOut && r.Policy == GreedyName)} by Greedy";
        o.WriteLine(string.Create(Inv, $"Fights played: {look} by Lookahead, {greedy} by Greedy; {settings.MinutesTaken:0.0} minutes with {settings.Workers} workers" +
                                       $"{(settings.Stopped ? " (stopped early)" : "")}. Fights that broke: {errors}; with a refused action: {refused}; lost to the turn limit: {timedOut}."));
        foreach (var tier in TierOrder)
            o.WriteLine($"  {tier}: light changes on {contexts.Count(c => c.Tier == tier && lookLight.Contains(c.Id))} fights, additions on {contexts.Count(c => c.Tier == tier && lookFull.Contains(c.Id))} (Lookahead); " +
                        $"Greedy {contexts.Count(c => c.Tier == tier && greedyLight.Contains(c.Id))} and {contexts.Count(c => c.Tier == tier && greedyFull.Contains(c.Id))}.");
        if (settings.Cards.Length > 0) o.WriteLine($"Only these additions were played: {settings.Cards}.");
        if (settings.Aa > 0) o.WriteLine($"Every fight was also played {settings.Aa} times with the same deck in another order.");
        o.WriteLine();
        o.WriteLine("How to read it. Every number is a difference between two fights that are the same but for one change to the deck:");
        o.WriteLine("win = change in win rate in percentage points, HP = change in mean HP left (a loss counts as 0, a win includes Burning Blood's 6),");
        o.WriteLine("each with its 95% interval from the paired differences. An interval that holds 0 means the change was not told apart from nothing.");
        o.WriteLine("\"vs Strike\" compares with the same fight with one more Strike instead: both decks have the same size, so both start from the same");
        o.WriteLine("shuffle with the card where the Strike would be, which takes a part of the draw luck out (compare the interval widths below). \"via Strike\" adds to that what one more");
        o.WriteLine("Strike is worth against the fight as it is (measured on all fights of the group): the same quantity as the direct figure, but tighter.");
        o.WriteLine("What the pairing does and does not remove. The two fights of a pair have the same deck but for the change, the same HP, potions, enemy and seed, so the");
        o.WriteLine("enemy's HP rolls and its stream of move rolls are the same. A deck of another size is shuffled into another order, so a direct pair (against the fight");
        o.WriteLine("as it is) holds two different deals; a pair against one more Strike starts from the same deal, but the two fights part as soon as the cards are played");
        o.WriteLine("differently. The intervals are worked out from the spread of the pair differences, fight by fight, so this noise is inside them.");
        o.WriteLine("What the intervals do not show. (1) Every card's figure uses the same plus-one-Strike fights (and the same fights as they are), so the luck of those");
        o.WriteLine("fights moves all figures up or down together; it does not change the order of the cards. Read a card against the reference lines below, not against 0.");
        o.WriteLine("(2) Of some 85 cards a few will sit beyond their interval by chance, and the cards at the two ends of a ranking are there partly by luck. (3) A win-rate");
        o.WriteLine("interval that rests on a handful of changed fights (upgrades of cards few decks hold) is not to be trusted. (4) The numbers are the worth of a card to");
        o.WriteLine("these scripted players, who look one turn ahead at most: see NUMBERS NOT TO TRUST. (5) The card is added to a deck that was not built around it: a card");
        o.WriteLine("that needs partners (Exhaust, Block, Vulnerable or HP-loss cards) is measured mostly without them.");
        o.WriteLine();

        // ---- the fights as they are ----
        o.WriteLine("THE FIGHTS AS THEY ARE");
        var baseTable = new TextTable("tier / enemy", "fights", "Lookahead wins", "HP left", "turns", "Greedy wins", "HP left ", "deck", "HP at start");
        foreach (var tier in TierOrder)
        {
            void Add(string name, List<Ctx> list)
            {
                var l = list.Where(c => lookLight.Contains(c.Id)).Select(c => byPolicy[LookaheadName].Get(c.Id, BaseId)).OfType<FightRow>().ToList();
                var g = list.Where(c => lookLight.Contains(c.Id)).Select(c => byPolicy[GreedyName].Get(c.Id, BaseId)).OfType<FightRow>().ToList();
                if (l.Count == 0) return;
                baseTable.Add(name, l.Count.ToString(Inv), Stats.Percent(l.Average(r => r.Won ? 1.0 : 0)), Stats.Mean(l.Select(r => (double)r.HpLeft)), Stats.Mean(l.Select(r => (double)r.Turns)),
                    g.Count > 0 ? Stats.Percent(g.Average(r => r.Won ? 1.0 : 0)) : "-", Stats.Mean(g.Select(r => (double)r.HpLeft)),
                    Stats.Mean(list.Select(c => (double)c.Setup.Deck.Count)), Stats.Mean(l.Select(r => (double)r.HpStart)));
            }
            var inTier = contexts.Where(c => c.Tier == tier).ToList();
            Add(tier, inTier);
            if (tier == Scenarios.NormalTier) continue;
            foreach (var encounter in inTier.Select(c => c.Setup.Encounter).Distinct().Order(StringComparer.Ordinal))
                Add("  " + encounter, inTier.Where(c => c.Setup.Encounter == encounter).ToList());
        }
        baseTable.Write(o);
        o.WriteLine();

        List<Line> Of(string policy, string sample, string group, string kind) =>
            lines.Where(l => l.Policy == policy && l.Sample == sample && l.Group == group && l.Kind == kind).ToList();

        WriteReference(o, lines, playRates);
        WriteSameDeck(o, contexts, byPolicy, settings);

        string[] AddCells(Line l) =>
        [
            l.CardName, l.Type, l.Rarity, l.Cost, l.Direct.Win.N.ToString(Inv),
            Interval(l.ViaWin), Interval(l.ViaHp), Interval(l.Direct.Win), Interval(l.Direct.Hp),
            l.VsStrike is null ? "-" : Interval(l.VsStrike.Win), l.VsStrike is null ? "-" : Interval(l.VsStrike.Hp),
        ];
        string[] addHeaders = ["card added", "type", "rarity", "cost", "fights", "win, via Strike", "HP, via Strike", "win, direct", "HP, direct", "win vs Strike", "HP vs Strike"];

        // Additions are ranked by the tighter figure of HP left: it moves with every fight, where a win only moves when the fight was close.
        static double Rank(Line l) => double.IsNaN(l.ViaHp.Mean) ? l.Direct.Hp.Mean : l.ViaHp.Mean;

        static double Median(IEnumerable<double> values)
        {
            var sorted = values.Where(v => !double.IsNaN(v)).Order().ToList();
            return sorted.Count == 0 ? double.NaN : sorted[sorted.Count / 2];
        }

        foreach (var (policy, sample) in new[] { (LookaheadName, "paired"), (GreedyName, "same fights as Lookahead"), (GreedyName, "all fights") })
        {
            var adds = Of(policy, sample, "elite+boss", "add").OrderByDescending(Rank).ToList();
            if (adds.Count == 0) continue;
            var measured = adds.Where(l => l.VsStrike is not null).ToList();
            o.WriteLine(string.Create(Inv, $"How wide the 95% intervals are, {policy} ({sample}), elites and bosses, the median over {measured.Count} additions, as plus or minus: ") +
                        string.Create(Inv, $"HP direct {Median(measured.Select(l => (l.Direct.Hp.Hi - l.Direct.Hp.Lo) / 2)):0.0}, vs Strike {Median(measured.Select(l => (l.VsStrike!.Hp.Hi - l.VsStrike.Hp.Lo) / 2)):0.0}, via Strike {Median(measured.Select(l => (l.ViaHp.Hi - l.ViaHp.Lo) / 2)):0.0}; ") +
                        string.Create(Inv, $"win direct {Median(measured.Select(l => (l.Direct.Win.Hi - l.Direct.Win.Lo) / 2)):0.0}, vs Strike {Median(measured.Select(l => (l.VsStrike!.Win.Hi - l.VsStrike.Win.Lo) / 2)):0.0}, via Strike {Median(measured.Select(l => (l.ViaWin.Hi - l.ViaWin.Lo) / 2)):0.0}."));
            o.WriteLine($"ONE CARD ADDED, ELITES AND BOSSES TOGETHER, {policy.ToUpperInvariant()} ({sample}): the 15 best and the 15 worst by HP left (via Strike)");
            var best = new TextTable(addHeaders);
            foreach (var l in adds.Take(15)) best.Add(AddCells(l));
            best.Write(o);
            o.WriteLine("  ...");
            var worst = new TextTable(addHeaders);
            foreach (var l in adds.TakeLast(15)) worst.Add(AddCells(l));
            worst.Write(o);
            o.WriteLine();

            var byWin = adds.OrderByDescending(l => double.IsNaN(l.ViaWin.Mean) ? l.Direct.Win.Mean : l.ViaWin.Mean).ToList();
            o.WriteLine($"The same ranked by win rate (via Strike), {policy}: best 15: " + string.Join(", ", byWin.Take(15).Select(l => $"{l.CardName} {Signed(double.IsNaN(l.ViaWin.Mean) ? l.Direct.Win.Mean : l.ViaWin.Mean)}")));
            o.WriteLine("  worst 15: " + string.Join(", ", byWin.TakeLast(15).Reverse().Select(l => $"{l.CardName} {Signed(double.IsNaN(l.ViaWin.Mean) ? l.Direct.Win.Mean : l.ViaWin.Mean)}")));
            o.WriteLine();

            o.WriteLine($"Groups of additions, {policy} (the mean over the group's cards, fight by fight):");
            var grouped = new TextTable("group", "tier", "fights", "win, via Strike", "HP, via Strike", "win, direct", "HP, direct");
            foreach (var group in new[] { "normal", "elite", "boss", "elite+boss" })
                foreach (var l in Of(policy, sample, group, "add-group"))
                    grouped.Add(l.CardName, group, l.Direct.Win.N.ToString(Inv), Interval(l.ViaWin), Interval(l.ViaHp), Interval(l.Direct.Win), Interval(l.Direct.Hp));
            grouped.Write(o);
            o.WriteLine();
        }

        // ---- Strike and Defend: one more, one less ----
        o.WriteLine("ONE STRIKE OR DEFEND MORE OR LESS (direct, on every fight)");
        var basics = new TextTable("change", "tier", "player", "fights", "win", "HP", "wins gained", "wins lost");
        foreach (var label in new[] { "remove:" + Strike, "remove:" + Defend, "add:" + Strike, "add:" + Defend })
            foreach (var group in new[] { "normal", "elite", "boss", "elite+boss" })
                foreach (var (policy, sample) in new[] { (LookaheadName, "paired"), (GreedyName, "same fights as Lookahead") })
                    foreach (var l in lines.Where(x => x.Policy == policy && x.Sample == sample && x.Group == group && x.Label == label && x.Kind is "basic" or "remove"))
                        basics.Add(label.Replace("Ironclad", ""), group, policy, l.Direct.Win.N.ToString(Inv), Interval(l.Direct.Win), Interval(l.Direct.Hp), l.Direct.Gained.ToString(Inv), l.Direct.Lost.ToString(Inv));
        basics.Write(o);
        o.WriteLine();

        // ---- upgrading a card of the deck ----
        o.WriteLine("ONE CARD OF THE DECK UPGRADED (same deck size, so the same shuffle; fights whose deck holds a plain copy)");
        foreach (var group in new[] { "elite+boss", "normal" })
        {
            o.WriteLine($"{group}, Lookahead; cards in at least 15 decks, by HP:");
            var upgrades = new TextTable("card upgraded", "type", "fights", "win", "HP", "wins gained", "wins lost", "Greedy: win", "Greedy: HP");
            var list = Of(LookaheadName, "paired", group, "upgrade").Concat(Of(LookaheadName, "paired", group, "upgrade-group"))
                .Where(l => l.Direct.Win.N >= 15).OrderByDescending(l => l.Kind == "upgrade-group" ? double.MaxValue : l.Direct.Hp.Mean).ToList();
            foreach (var l in list)
            {
                var g = lines.FirstOrDefault(x => x.Policy == GreedyName && x.Sample == "same fights as Lookahead" && x.Group == group && x.Label == l.Label);
                upgrades.Add(l.CardName + (l.Direct.Win.N < FewPairs ? " *" : ""), l.Type, l.Direct.Win.N.ToString(Inv), Interval(l.Direct.Win), Interval(l.Direct.Hp), l.Direct.Gained.ToString(Inv), l.Direct.Lost.ToString(Inv),
                    g is null ? "-" : Interval(g.Direct.Win), g is null ? "-" : Interval(g.Direct.Hp));
            }
            upgrades.Write(o);
            o.WriteLine(string.Create(Inv, $"* fewer than {FewPairs} fights: the interval is wide, and a win figure from a handful of changed fights means little."));
            o.WriteLine();
        }

        // ---- an added card upgraded against the same card plain ----
        o.WriteLine("AN ADDED CARD UPGRADED AGAINST THE SAME CARD ADDED PLAIN (same shuffle), ELITES AND BOSSES, LOOKAHEAD: the 15 largest and 10 smallest by HP");
        var ups = Of(LookaheadName, "paired", "elite+boss", "addup").Where(l => l.VsPlain is not null).OrderByDescending(l => l.VsPlain!.Hp.Mean).ToList();
        var upTable = new TextTable("card", "type", "fights", "win: + against plain", "HP: + against plain", "the + added, HP via Strike");
        foreach (var l in ups.Take(15)) upTable.Add(l.CardName, l.Type, l.VsPlain!.Win.N.ToString(Inv), Interval(l.VsPlain.Win), Interval(l.VsPlain.Hp), Interval(l.ViaHp));
        upTable.Write(o);
        o.WriteLine("  ...");
        var downTable = new TextTable("card", "type", "fights", "win: + against plain", "HP: + against plain", "the + added, HP via Strike");
        foreach (var l in ups.TakeLast(10)) downTable.Add(l.CardName, l.Type, l.VsPlain!.Win.N.ToString(Inv), Interval(l.VsPlain.Win), Interval(l.VsPlain.Hp), Interval(l.ViaHp));
        downTable.Write(o);
        foreach (var l in Of(LookaheadName, "paired", "elite+boss", "addup-group").Where(l => l.VsPlain is not null))
            o.WriteLine($"  {l.CardName}: the upgrade is worth win {Interval(l.VsPlain!.Win)}, HP {Interval(l.VsPlain.Hp)} over {l.VsPlain.Win.N} fights.");
        o.WriteLine();

        // ---- does the player play the card at all ----
        o.WriteLine("DOES LOOKAHEAD PLAY THE CARD? (the card added plain; elites and bosses; a turn counts when a copy was in hand at a decision)");
        o.WriteLine("The 15 cards named before the run as the ones a one-turn player would undervalue most, then some plain cards to compare with.");
        var play = new TextTable("card", "type", "cost", "fights", "in hand in", "played in", "% of those fights", "turns in hand", "% of turns played", "first in hand", "first played", "HP, via Strike");
        void AddPlay(string card)
        {
            if (playRates.FirstOrDefault(r => r.Policy == LookaheadName && r.Group == "elite+boss" && r.Card == card && !r.Upgraded) is not { } r) return;
            var def = registry.Card(card);
            var line = lines.FirstOrDefault(l => l.Policy == LookaheadName && l.Group == "elite+boss" && l.Label == "add:" + card && l.Kind == "add");
            play.Add(def.Name, def.Type.ToString(), def.CostsX ? "X" : def.Cost.ToString(Inv), r.Fights.ToString(Inv), r.FightsInHand.ToString(Inv), r.FightsPlayed.ToString(Inv),
                r.FightsInHand > 0 ? Stats.Percent((double)r.FightsPlayed / r.FightsInHand) : "-", r.HandTurns.ToString(Inv),
                r.HandTurns > 0 ? Stats.Percent((double)r.PlayTurns / r.HandTurns) : "-", F(r.MeanFirstHandTurn), F(r.MeanFirstPlayTurn),
                line is null ? "-" : Interval(double.IsNaN(line.ViaHp.Mean) ? line.Direct.Hp : line.ViaHp));
        }
        foreach (var card in ExpectedUndervalued) AddPlay(card);
        play.Add("---");
        foreach (var card in new[] { Strike, Defend, "Bash", "Inflame", "StoneArmor", "ShrugItOff", "Bludgeon", "Impervious" }) AddPlay(card);
        play.Write(o);
        var powers = playRates.Where(r => r.Policy == LookaheadName && r.Group == "elite+boss" && !r.Upgraded && registry.Card(r.Card).Type == CardType.Power).ToList();
        if (powers.Count > 0)
            o.WriteLine(string.Create(Inv, $"All {powers.Count} Powers together: played in {100.0 * powers.Sum(r => r.FightsPlayed) / Math.Max(1, powers.Sum(r => r.FightsInHand)):0}% of the fights they came to hand in."));
        o.WriteLine();

        // ---- by enemy ----
        o.WriteLine($"BY ENEMY (Lookahead; additions with at least {MinEnemyPairs} fights against the enemy): the 6 best and 6 worst by HP, via Strike");
        foreach (var encounter in contexts.Where(c => c.Tier != Scenarios.NormalTier).Select(c => c.Setup.Encounter).Distinct().Order(StringComparer.Ordinal))
        {
            var adds = Of(LookaheadName, "paired", encounter, "add").OrderByDescending(Rank).ToList();
            if (adds.Count == 0)
            {
                o.WriteLine($"{encounter}: fewer than {MinEnemyPairs} fights with additions.");
                continue;
            }
            o.WriteLine($"{encounter} ({adds[0].Direct.Win.N} fights): best " + string.Join(", ", adds.Take(6).Select(l => $"{l.CardName} {Signed(Rank(l))}")) +
                        "; worst " + string.Join(", ", adds.TakeLast(6).Reverse().Select(l => $"{l.CardName} {Signed(Rank(l))}")));
        }
    }
}
