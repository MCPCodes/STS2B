using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

// The forecast's promises (Sim/Forecast.cs), held against what the enemies then do.
//
// Before every "end_turn" of every fight the forecast of each enemy is worked out, as a
// snapshot with the switch on shows it, whether or not the switch is on. A forecast made on
// turn N speaks of the turns N+1, N+2 and N+3 (the move it shows is turn N's own, and the
// intent check holds that). It is judged twice.
//
// 1. With the player passing. The forecast says what happens "if you change nothing", so the
//    fight is copied at that moment and the copy's player only ends its turns (with HP enough
//    to live through them). Step k of the forecast is held against what the enemy shows on the
//    copy's turn N+k: the same move, and for every number without "~" the same words. This is
//    the strict half. A difference is only let through with a cause that can be told from the
//    copy's own log: a move was forced on the enemy (the player's standing effects, a Thorns
//    potion or the like, took it to its HP threshold or to 0 HP), or one of the player's own
//    standing effects changed a power that shapes the enemy's hit (a card that a power plays
//    by itself). Where the forecast ends in a random pick, the move made must be one of those
//    listed, and the chances must be the roll's own weights against its own log.
// 2. In the fight as it was played. Step k is held against the move the enemy makes on turn
//    N+k and the intent it shows when that turn is ended. Here the player acts in between, so
//    more causes are honest: a forced move, a modifier the player changed (the powers the
//    player's cards and potions touched since the forecast, or any difference to the copy in
//    which the player passed). Over all fights the first listed option of the random picks
//    must have come about as often as its chance said.
//
// And every time: working a forecast out may use no random number and change no move log;
// the same forecast must come out of a copy of the fight in which everything the player
// cannot know (the random numbers to come, the order of the draw pile) is replaced; and the
// snapshot with the switch on must show exactly what was worked out.
public static partial class Honesty
{
    /// <summary>What the forecasts promised over all fights, and what became of it.</summary>
    public sealed class ForecastCounts
    {
        /// <summary>"end_turn" snapshots looked at; enemies in them; how many got at least one certain
        /// move, how many a chance entry, how many no forecast at all.</summary>
        public int Snapshots, Enemies, WithMoves, WithOdds, WithNothing;

        /// <summary>Certain steps forecast, and how many of them carry a number with "~".</summary>
        public int Steps, StepsRough;

        // ----- with the player passing -----

        /// <summary>Certain steps that came due on the copy, and how many showed the forecast move.</summary>
        public int PassMoves, PassMovesKept;

        /// <summary>Of those, the ones without "~": how many read word for word as forecast.</summary>
        public int PassTexts, PassTextsKept;

        /// <summary>Of those, the ones with "~": how many came out at the marked number all the same.</summary>
        public int PassRough, PassRoughSame;

        /// <summary>Chance entries that came due on the copy; how many gave a listed move; how many
        /// were held against the roll's own weights, and how many of those agreed.</summary>
        public int PassPicks, PassPicksListed, PassOdds, PassOddsExact;

        /// <summary>Steps never judged on the copy: the enemy or the copy's fight was gone first.</summary>
        public int PassUnreached;

        // ----- in the fight as played -----

        /// <summary>Certain steps that came due, and how many were the move the enemy made.</summary>
        public int Moves, MovesKept;

        /// <summary>Of those, the ones without "~": how many read word for word as the intent shown when the move came.</summary>
        public int Texts, TextsKept;

        /// <summary>Of those, the ones with "~": how many came out at the marked number all the same.</summary>
        public int Rough, RoughSame;

        /// <summary>Chance entries that came due, how many gave a listed move, and how many of those
        /// came after a forced move had shifted the enemy's course (left out of the count below).</summary>
        public int Picks, PicksListed, PicksShifted;

        /// <summary>Over the picks: how often the first listed option came, how often the chances
        /// said it would, and the variance of that count.</summary>
        public int FirstCame;
        public double FirstExpected, FirstVariance;

        /// <summary>Steps never judged: the enemy or the fight was gone before their turn.</summary>
        public int Unreached;

        // ----- every time -----

        /// <summary>Forecasts worked out again on a copy without what the player cannot know, and how many came out the same.</summary>
        public int Blind, BlindSame;

        /// <summary>Times working out a forecast used a random number or changed a machine. Must be 0.</summary>
        public int Disturbed;
    }

    /// <summary>The forecast's rows of the honesty table: what was checked, how many kept.</summary>
    public static void AddForecastRows(Report report, Action<string, int, int> row)
    {
        var f = report.Forecast;
        row("forecast, player passing: the move shown, turns later", f.PassMoves, f.PassMovesKept);
        row("forecast, player passing: words and numbers without ~", f.PassTexts, f.PassTextsKept);
        row("forecast, player passing: a pick gave a listed move", f.PassPicks, f.PassPicksListed);
        row("forecast, player passing: chances are the roll's weights", f.PassOdds, f.PassOddsExact);
        row("forecast, as played: the move the enemy made", f.Moves, f.MovesKept);
        row("forecast, as played: words and numbers without ~", f.Texts, f.TextsKept);
        row("forecast, as played: a pick gave a listed move", f.Picks, f.PicksListed);
        row("forecast: the same without what the player cannot know", f.Blind, f.BlindSame);
    }

    /// <summary>What the forecasts covered, in a few sentences under the table.</summary>
    public static void WriteForecast(Report report, TextWriter output)
    {
        var f = report.Forecast;
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"(Forecasts, as --forecast shows them, were worked out before every end_turn: {f.Snapshots} snapshots, {f.Enemies} enemies, of which {f.WithMoves} got at least one certain move, " +
            $"{f.WithOdds} a chance entry and {f.WithNothing} nothing. Of {f.Steps} certain steps {f.StepsRough} carry a number with ~. " +
            $"With the player passing, {f.PassRoughSame} of {f.PassRough} numbers with ~ came out as marked all the same and {f.PassUnreached} steps never came due; " +
            $"as played, {f.RoughSame} of {f.Rough} and {f.Unreached}. " +
            $"Over the {f.PicksListed - f.PicksShifted} picks that no forced move had shifted, the first listed move came {f.FirstCame} times; the chances said {f.FirstExpected:0.0}. " +
            $"Working a forecast out used a random number or changed a machine {f.Disturbed} times.)"));
    }

    /// <summary>The first listed option of the random picks must have come as often as its chances
    /// said, within four standard deviations; otherwise the chances are wrong.</summary>
    private static void JudgeChances(Report report)
    {
        var f = report.Forecast;
        var off = Math.Abs(f.FirstCame - f.FirstExpected);
        if (f.Picks > 0 && off > 4 * Math.Sqrt(f.FirstVariance) + 1)
            report.Unexplained.Add(string.Create(CultureInfo.InvariantCulture,
                $"forecast chances: over {f.PicksListed - f.PicksShifted} random picks the first listed move came {f.FirstCame} times, and the chances said {f.FirstExpected:0.0}"));
    }

    private sealed class ForecastWatch(string where, Report report)
    {
        /// <summary>The HP the passing player of a copy is given: enough to live through three enemy turns of anything.</summary>
        private const int PassingHp = 1_000_000;

        /// <summary>
        /// What decides how hard an enemy's move hits the player at one moment: the damage chain's
        /// answer for three plain amounts (which fixes what it adds, multiplies by and caps at,
        /// whoever does it), and what each power on the enemy and on the player does to such a
        /// hit, by power and owner, for naming what changed.
        /// </summary>
        private sealed record Prints(decimal At10, decimal At20, decimal At1000, Dictionary<string, string> Powers)
        {
            public bool SameAnswer(Prints other) => At10 == other.At10 && At20 == other.At20 && At1000 == other.At1000;
        }

        /// <summary>What an enemy showed on a turn of the copy in which the player passes.</summary>
        /// <param name="RollOdds">Where the roll that led here was a random pick: the roll's own weights over their sum, taken before it.</param>
        private sealed record Passed(string? MoveId, string Text, bool Forced, Prints Prints, HashSet<string> Touched, IReadOnlyList<(string Id, double Chance)>? RollOdds);

        /// <summary>A forecast made at an "end_turn", waiting for its turns to come.</summary>
        private sealed record Made(int RosterIndex, int CreatureId, string Name, int Turn, int Forced, int Seq, Forecast.Reading Reading, Prints Prints)
        {
            /// <summary>What the copy showed on the turns after: index 0 is step 1. Null where the enemy was not in the copy's fight.</summary>
            public List<Passed?> Pass { get; } = new();

            public int StepCount => Reading.Steps.Count + (Reading.Odds.Count > 0 ? 1 : 0);
        }

        /// <summary>What an enemy showed at the "end_turn" that is being judged.</summary>
        private sealed record Shown(string? MoveId, string Text, int Forced, Prints Prints);

        private readonly List<Made> _open = new();
        private readonly List<Made> _new = new();
        private readonly Dictionary<int, Shown> _shown = new();
        private readonly List<(int Seq, string Key)> _touched = new();
        private int _turn;
        private int _seq;

        // ---------- before the turn is ended ----------

        /// <summary>Notes what every enemy shows now, works out the forecasts a snapshot would carry,
        /// and judges them on a copy of the fight in which the player passes.</summary>
        public void BeforeEndTurn(Fight fight, GameSnapshot snapshot)
        {
            _turn = fight.Turn;
            _shown.Clear();
            _new.Clear();
            var counts = report.Forecast;
            counts.Snapshots++;

            var rolls = Rolls(fight);
            var machines = Machines(fight);

            // What the model would read: the same snapshot with the switch on.
            var shown = new GameSnapshot { Screen = snapshot.Screen, AwaitingDecision = true, State = (JsonObject)snapshot.State.DeepClone(), Actions = snapshot.Actions };
            Forecast.AddTo(fight, shown);
            var shownNext = new Dictionary<int, List<string>>();
            foreach (var node in shown.State["enemies"] as JsonArray ?? [])
                if (node is JsonObject entry && entry[Forecast.NextKey] is JsonArray next) shownNext[Int(entry["i"])] = next.Select(n => Str(n) ?? "").ToList();

            // The same fight without what the player cannot know: the random numbers to come and the draw order.
            var blind = fight.CloneDeterminized(unchecked(fight.Seed * 31 + _turn));

            for (var j = 0; j < fight.Roster.Count; j++)
            {
                var enemy = fight.Roster[j];
                if (!fight.InFight(enemy) || enemy.Monster is not { } monster) continue;
                var prints = PrintsOf(fight, enemy);
                _shown[enemy.Id] = new Shown(monster.NextMove?.Id, string.Join(" + ", fight.IntentTexts(enemy)), monster.ForcedMoves, prints);

                var reading = Forecast.Read(fight, enemy);
                var lines = reading?.Lines ?? [];
                counts.Enemies++;
                if (reading is { Steps.Count: > 0 }) counts.WithMoves++;
                if (reading is { Odds.Count: > 0 }) counts.WithOdds++;
                if (reading is null) counts.WithNothing++;
                counts.Steps += reading?.Steps.Count ?? 0;
                counts.StepsRough += reading?.Steps.Count(s => s.Rough) ?? 0;

                // The snapshot must say exactly this, no more and no less, and never an empty list.
                if (!lines.SequenceEqual(shownNext.GetValueOrDefault(j) ?? []) || (reading is not null && lines.Count == 0) || lines.Count > Forecast.MostSteps)
                    report.Unexplained.Add($"{where}, turn {_turn}: the snapshot shows another forecast for {enemy.Name} than was worked out ([{string.Join(", ", lines)}])");

                counts.Blind++;
                var unknowing = Forecast.For(blind, blind.Roster[j]) ?? [];
                if (lines.SequenceEqual(unknowing)) counts.BlindSame++;
                else report.Unexplained.Add($"{where}, turn {_turn}: {enemy.Name}'s forecast [{string.Join(", ", lines)}] reads [{string.Join(", ", unknowing)}] once what the player cannot know is replaced");

                if (reading is not null) _new.Add(new Made(j, enemy.Id, enemy.Name, _turn, monster.ForcedMoves, _seq, reading, prints));
            }

            if (shown.State.ContainsKey(Forecast.HintKey) != (_new.Count > 0))
                report.Unexplained.Add($"{where}, turn {_turn}: the snapshot's {Forecast.HintKey} is there without a forecast, or missing with one");

            if (Rolls(fight) != rolls || Machines(fight) != machines)
            {
                counts.Disturbed++;
                report.Unexplained.Add($"{where}, turn {_turn}: working out the forecasts used a random number or changed a machine");
            }

            Pass(fight);
            foreach (var made in _new) JudgePassing(made);
        }

        /// <summary>Plays a copy of the fight on with a player that only ends its turns, and notes what every enemy with a forecast shows on each of them.</summary>
        private void Pass(Fight fight)
        {
            if (_new.Count == 0) return;
            var copy = fight.Clone();
            // The passing player must live through the turns that are judged; its HP has no say in what an enemy does.
            copy.Player.MaxHp = Math.Max(copy.Player.MaxHp, PassingHp);
            copy.Player.Hp = PassingHp;
            var touched = new HashSet<string>(StringComparer.Ordinal);
            copy.HistoryTraced = e =>
            {
                if (IsPlayersDoing(e)) touched.Add(Key(e.Name, e.ActorId));
            };

            var steps = _new.Max(m => m.StepCount);
            for (var k = 1; k <= steps; k++)
            {
                // Where this end of turn leads a forecast into its random pick: what the roll itself will weigh.
                var odds = new Dictionary<int, IReadOnlyList<(string Id, double Chance)>?>();
                foreach (var made in _new)
                    if (made.Reading.Odds.Count > 0 && made.StepCount == k) odds[made.CreatureId] = RollOdds(copy.Roster[made.RosterIndex]);

                if (copy.IsOver || copy.HasPendingChoice || !copy.Execute("end_turn").Ok || copy.IsOver || copy.HasPendingChoice) break;

                foreach (var made in _new)
                {
                    var enemy = copy.Roster[made.RosterIndex];
                    made.Pass.Add(copy.InFight(enemy) && enemy.Monster is { } monster
                        ? new Passed(monster.NextMove?.Id, string.Join(" + ", copy.IntentTexts(enemy)), monster.ForcedMoves > made.Forced, PrintsOf(copy, enemy),
                            new HashSet<string>(touched, StringComparer.Ordinal), odds.GetValueOrDefault(made.CreatureId))
                        : null);
                }
            }
        }

        private void JudgePassing(Made made)
        {
            var counts = report.Forecast;
            var reading = made.Reading;
            for (var k = 1; k <= made.StepCount; k++)
            {
                if (k > made.Pass.Count || made.Pass[k - 1] is not { } at)
                {
                    counts.PassUnreached += made.StepCount - k + 1;
                    return;
                }

                if (k <= reading.Steps.Count)
                {
                    var promised = reading.Steps[k - 1];
                    var example = $"{where}: on turn {made.Turn} {made.Name}'s move {k} turns on was forecast as '{promised.Text}' ({promised.MoveId}); with the player passing it showed '{at.Text}' ({at.MoveId}) on turn {made.Turn + k}";
                    counts.PassMoves++;
                    if (at.MoveId != promised.MoveId)
                    {
                        if (at.Forced) report.Explain("forecast, player passing: another move came, because a move was forced on the enemy after the forecast (the player's standing effects took it to its HP threshold or to 0 HP)", example);
                        else report.Unexplained.Add(example + ": nothing had forced a move on it");
                        // What follows a forced or wrong move is no longer this forecast's.
                        counts.PassUnreached += made.StepCount - k;
                        return;
                    }
                    counts.PassMovesKept++;
                    JudgePassingText(made, promised.Text, promised.Rough, at, example);
                    continue;
                }

                var options = reading.Odds;
                var said = $"{where}: on turn {made.Turn} {made.Name}'s pick {k} turns on was forecast as '{reading.Lines[^1]}'; with the player passing it showed '{at.Text}' ({at.MoveId}) on turn {made.Turn + k}";
                counts.PassPicks++;
                if (options.FirstOrDefault(o => o.MoveId == at.MoveId) is not { } option)
                {
                    if (at.Forced) report.Explain("forecast, player passing: the pick gave no listed move, because a move was forced on the enemy after the forecast", said);
                    else report.Unexplained.Add(said + ": that move was not among those listed, and nothing had forced a move on it");
                    return;
                }
                counts.PassPicksListed++;
                if (!at.Forced)
                {
                    counts.PassOdds++;
                    if (at.RollOdds is { } truth && SameOdds(truth, options)) counts.PassOddsExact++;
                    else report.Unexplained.Add(said + $": the roll's own weights were [{Describe(at.RollOdds)}]");
                }
                JudgePassingText(made, option.Text, option.Rough, at, said);
            }
        }

        private void JudgePassingText(Made made, string text, bool rough, Passed at, string example)
        {
            var counts = report.Forecast;
            if (rough)
            {
                counts.PassRough++;
                if (Unmarked(text) == at.Text) counts.PassRoughSame++;
                return;
            }
            counts.PassTexts++;
            if (text == at.Text)
            {
                counts.PassTextsKept++;
                return;
            }
            if (at.Forced)
                report.Explain("forecast, player passing: the move came with other numbers, because a move was forced on the enemy in between (what it lost with the stun was not foreseen)", example);
            else if (Named(Changed(made.Prints, at.Prints).Where(at.Touched.Contains)) is { Length: > 0 } names)
                report.Explain($"forecast, player passing: the move came with other numbers, because one of the player's own standing effects changed a modifier in between ({names})", example);
            else report.Unexplained.Add(example + ": nothing was forced on it, and no effect of the player's changed what shapes its hit");
        }

        // ---------- after every action ----------

        /// <summary>Notes which powers the player's own cards, potions and effects changed in the action that just ran.</summary>
        public void AfterAction(List<Event> events)
        {
            _seq++;
            foreach (var ev in events)
                if (ev.Entry is { } e && IsPlayersDoing(e)) _touched.Add((_seq, Key(e.Name, e.ActorId)));
        }

        /// <summary>The turn noted last has been ended: judges every step that was due in it, then takes the turn's own forecasts on.</summary>
        public void AfterEndTurn(Fight fight, List<Event> events)
        {
            var performed = new Dictionary<int, string>();
            foreach (var ev in events)
                if (ev.Entry is { Kind: HistoryKind.MonsterPerformedMove, Side: Side.Enemy } e) performed[e.ActorId] = e.Name ?? "";

            var counts = report.Forecast;
            for (var n = _open.Count - 1; n >= 0; n--)
            {
                var made = _open[n];
                var step = _turn - made.Turn;
                if (!_shown.TryGetValue(made.CreatureId, out var shown))
                {
                    // It was gone before this turn was ended: whatever was still to come is never judged.
                    counts.Unreached += made.StepCount - (step - 1);
                    _open.RemoveAt(n);
                    continue;
                }

                var monster = fight.Roster[made.RosterIndex].Monster!;
                var did = performed.TryGetValue(made.CreatureId, out var move) ? move : shown.MoveId ?? "";
                var forced = monster.ForcedMoves > made.Forced;
                var passed = step <= made.Pass.Count ? made.Pass[step - 1] : null;
                if (step <= made.Reading.Steps.Count) JudgeMove(made, step, shown, did, forced, passed);
                else if (step == made.StepCount) JudgePick(made, step, shown, did, forced, passed);
                if (step >= made.StepCount) _open.RemoveAt(n);
            }

            _open.AddRange(_new);
            _new.Clear();
        }

        private void JudgeMove(Made made, int step, Shown shown, string did, bool forced, Passed? passed)
        {
            var counts = report.Forecast;
            var promised = made.Reading.Steps[step - 1];
            var example = $"{where}: on turn {made.Turn} {made.Name}'s move {step} turns on was forecast as '{promised.Text}' ({promised.MoveId}); on turn {_turn} it showed '{shown.Text}' and made {did}";
            counts.Moves++;
            if (did != promised.MoveId)
            {
                if (forced) report.Explain("forecast, as played: another move was made, because a move was forced on the enemy after the forecast (a stun at its HP threshold, coming back from 0 HP), which no forecast foresees", example);
                else report.Unexplained.Add(example + ": nothing had forced a move on it");
                return;
            }
            counts.MovesKept++;
            if (shown.MoveId == promised.MoveId) JudgeText(made, promised.Text, promised.Rough, shown, passed, example);
        }

        private void JudgePick(Made made, int step, Shown shown, string did, bool forced, Passed? passed)
        {
            var counts = report.Forecast;
            var options = made.Reading.Odds;
            var example = $"{where}: on turn {made.Turn} {made.Name}'s pick {step} turns on was forecast as '{made.Reading.Lines[^1]}'; on turn {_turn} it showed '{shown.Text}' and made {did}";
            counts.Picks++;
            if (options.FirstOrDefault(o => o.MoveId == did) is not { } option)
            {
                if (forced) report.Explain("forecast, as played: the pick gave no listed move, because a move was forced on the enemy after the forecast", example);
                else report.Unexplained.Add(example + ": that move was not among those listed, and nothing had forced a move on it");
                return;
            }
            counts.PicksListed++;
            // A forced move shifts what comes when; such a pick says nothing about the chances.
            if (forced)
            {
                counts.PicksShifted++;
                return;
            }
            var first = options[0];
            counts.FirstExpected += first.Probability;
            counts.FirstVariance += first.Probability * (1 - first.Probability);
            if (did == first.MoveId) counts.FirstCame++;
            if (shown.MoveId == option.MoveId) JudgeText(made, option.Text, option.Rough, shown, passed, example);
        }

        private void JudgeText(Made made, string text, bool rough, Shown shown, Passed? passed, string example)
        {
            var counts = report.Forecast;
            if (rough)
            {
                counts.Rough++;
                if (Unmarked(text) == shown.Text) counts.RoughSame++;
                return;
            }
            counts.Texts++;
            if (text == shown.Text)
            {
                counts.TextsKept++;
                return;
            }

            if (shown.Forced > made.Forced)
            {
                report.Explain("forecast, as played: the move came with other numbers, because a move was forced on the enemy in between (what it lost with the stun was not foreseen)", example);
                return;
            }
            // The powers the player's own cards, potions and effects touched since the forecast, where they now do something else to this enemy's hit.
            var touched = _touched.Where(t => t.Seq > made.Seq).Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
            if (Named(Changed(made.Prints, shown.Prints).Where(touched.Contains)) is { Length: > 0 } names)
            {
                report.Explain($"forecast, as played: the move came with other numbers, because the player changed a modifier in between ({names})", example);
                return;
            }
            // Or the hit is shaped otherwise than in the copy where the player passed: then what the player did made the difference.
            if (passed is not null && !passed.Prints.SameAnswer(shown.Prints))
            {
                var differs = Named(Changed(passed.Prints, shown.Prints));
                report.Explain($"forecast, as played: the move came with other numbers, because what the player did in between changed a modifier ({(differs.Length > 0 ? differs : "not a power")})", example);
                return;
            }
            report.Unexplained.Add(example + ": no move was forced on it, and the player changed nothing that shapes its hit");
        }

        /// <summary>The fight is over: what was still to come is never judged.</summary>
        public void Close()
        {
            foreach (var made in _open.Concat(_new))
                report.Forecast.Unreached += Math.Max(0, made.StepCount - Math.Max(0, _turn - made.Turn));
            _open.Clear();
            _new.Clear();
        }

        // ---------- helpers ----------

        /// <summary>A power landed or changed, and the player was the one who did it.</summary>
        private static bool IsPlayersDoing(HistoryEntry e) => e is { Kind: HistoryKind.PowerReceived, OtherId: PlayerId } && e.Amount != 0;

        private static string Key(string? powerClass, int ownerId) => string.Create(CultureInfo.InvariantCulture, $"{powerClass ?? "?"}@{ownerId}");

        /// <summary>"Strength on the enemy, Vulnerable on the player" for keys of <see cref="Key"/>.</summary>
        private static string Named(IEnumerable<string> keys) => string.Join(", ", keys
            .Select(k => k.Split('@'))
            .Select(p => $"{p[0].Replace("Power", "", StringComparison.Ordinal)} on {(p[1] == "0" ? "the player" : "the enemy")}")
            .Distinct().Order(StringComparer.Ordinal));

        private static string Unmarked(string text) => text.Replace("~", "", StringComparison.Ordinal);

        /// <summary>Random numbers used so far, over all streams.</summary>
        private static int Rolls(Fight f)
        {
            var r = f.Rng;
            return r.Shuffle.Counter + r.MonsterAi.Counter + r.Niche.Counter + r.CombatTargets.Counter + r.CombatCardSelection.Counter
                   + r.CombatCardGeneration.Counter + r.CombatPotionGeneration.Counter + r.CombatEnergyCosts.Counter + r.Encounter.Counter;
        }

        /// <summary>Every machine as it stands: the move showing, the log, the forced moves so far.</summary>
        private static string Machines(Fight f) => string.Join("|", f.Roster.Select(e => e.Monster is { } m
            ? string.Create(CultureInfo.InvariantCulture, $"{m.NextMove?.Id}:{string.Join(",", m.MoveLog)}:{m.ForcedMoves}:{m.ShownMovePerformed}")
            : ""));

        private static Prints PrintsOf(Fight f, Creature enemy)
        {
            const decimal probe = 10m;
            var powers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var owner in new[] { enemy, f.Player })
            {
                foreach (var power in owner.Powers)
                {
                    var adds = power.ModifyDamageAdditive(f, f.Player, probe, Props.MonsterMove, enemy, null);
                    var times = power.ModifyDamageMultiplicative(f, f.Player, probe, Props.MonsterMove, enemy, null);
                    var cap = power.ModifyDamageCap(f, f.Player, Props.MonsterMove, enemy, null);
                    if (adds != 0m || times != 1m || cap != decimal.MaxValue)
                        powers[Key(power.ClassName, owner.Id)] = string.Create(CultureInfo.InvariantCulture, $"{adds}/{times}/{cap}");
                }
            }
            return new Prints(
                f.ModifyDamage(f.Player, enemy, 10m, Props.MonsterMove, null),
                f.ModifyDamage(f.Player, enemy, 20m, Props.MonsterMove, null),
                f.ModifyDamage(f.Player, enemy, 1000m, Props.MonsterMove, null),
                powers);
        }

        /// <summary>The powers (by <see cref="Key"/>) that do something else to the enemy's hit in one moment than in the other.</summary>
        private static IEnumerable<string> Changed(Prints then, Prints now) =>
            then.Powers.Keys.Union(now.Powers.Keys).Where(k => then.Powers.GetValueOrDefault(k) != now.Powers.GetValueOrDefault(k));

        /// <summary>
        /// What the roll itself will weigh when the move this enemy shows is followed by a random
        /// branch: each open option's weight over the sum, by the roll's own routine against the
        /// real move log (all blocked: the first listed, certain). Null when no random branch follows.
        /// </summary>
        private static IReadOnlyList<(string Id, double Chance)>? RollOdds(Creature enemy)
        {
            if (enemy.Monster is not { NextMove: { } move } monster) return null;
            if (monster.FollowUpIdOf(move) is not { Length: > 0 } id || monster.StateById(id) is not RandomBranch branch) return null;

            var weights = new List<(string Id, float Weight)>();
            for (var i = 0; i < branch.OptionCount; i++) weights.Add((branch.OptionAt(i).StateId, branch.RollWeight(i, monster.MoveLog)));
            // The roll adds the weights up in double and narrows the sum to float once.
            var total = (float)weights.Sum(w => (double)w.Weight);
            if (total <= 0f) return [(weights[0].Id, 1.0)];
            return weights.Where(w => w.Weight > 0f).GroupBy(w => w.Id).Select(g => (g.Key, g.Sum(w => (double)w.Weight / total))).ToList();
        }

        private static bool SameOdds(IReadOnlyList<(string Id, double Chance)> truth, IReadOnlyList<Forecast.Chance> said) =>
            truth.Count == said.Count && truth.Zip(said).All(p => p.First.Id == p.Second.MoveId && Math.Abs(p.First.Chance - p.Second.Probability) < 1e-9);

        private static string Describe(IReadOnlyList<(string Id, double Chance)>? odds) =>
            odds is null ? "no random branch" : string.Join(", ", odds.Select(o => string.Create(CultureInfo.InvariantCulture, $"{o.Id} {o.Chance:0.###}")));
    }
}
