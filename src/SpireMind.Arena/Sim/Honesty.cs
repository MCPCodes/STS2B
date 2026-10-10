using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The predicted-versus-actual check: does a snapshot keep what it promises? Before every
/// action it notes what the model was shown: before "end_turn" each enemy's intents and the
/// incoming damage, before a card its cost and the numbers of its text (its "vs" text for the
/// enemy it is aimed at), before a potion the numbers of its text. Then the action runs, and
/// what really happened is compared with the note: every hit's amount as the damage chain left
/// it (before Block), the hits per enemy, the Block gained, the energy paid, the status cards
/// added, and whether a "defend", "buff" or "debuff" did anything of the kind.
/// <para>
/// A difference is either a fault of the simulator or a property of the game's own display
/// that the real mod shares: an intent is worked out when the snapshot is taken, so whatever
/// happens between the snapshot and the hit can change it; an attack is cut short when its
/// target or its attacker dies; a card that picks its own target is shown without one. Every
/// difference is given a cause it can be told by from what happened before it; one that fits
/// none is listed as unexplained, and the check fails on any.
/// </para>
/// </summary>
public static partial class Honesty
{
    /// <summary>What the check found, over all fights.</summary>
    public sealed class Report
    {
        public int Fights, Decisions, Refused;
        public int EnemyTurns, IntentAttacks, IntentAttacksKept, IncomingTotals, IncomingTotalsKept, OtherIntents, OtherIntentsKept;
        public int CardPlays, CardDamage, CardDamageKept, CardBlock, CardBlockKept, CardCosts, CardCostsKept, PotionUses, PotionNumbers, PotionNumbersKept;

        /// <summary>Differences with a known cause: the cause, how often, and one example.</summary>
        public SortedDictionary<string, (int Count, string Example)> Explained { get; } = new(StringComparer.Ordinal);

        /// <summary>Differences no known cause explains. Each is a fault to look into.</summary>
        public List<string> Unexplained { get; } = new();

        /// <summary>Fights that threw.</summary>
        public List<string> Errors { get; } = new();

        /// <summary>Fights lost to the arena's turn limit, not by dying: each is worth a look.</summary>
        public List<string> TurnLimit { get; } = new();

        public int Won;

        public bool Passed => Unexplained.Count == 0 && Errors.Count == 0 && Refused == 0;

        internal void Explain(string cause, string example)
        {
            Explained[cause] = Explained.TryGetValue(cause, out var known) ? (known.Count + 1, known.Example) : (1, example);
        }
    }

    private const int MostDecisions = 6000;
    private const int PlayerId = 0;

    /// <summary>One thing that happened during an action, in the order it happened: a history entry, or a damage instance.</summary>
    private readonly record struct Event(HistoryEntry? Entry, DamageTrace? Damage)
    {
        public bool IsKind(HistoryKind kind) => Entry is { } e && e.Kind == kind;
    }

    /// <summary>Plays every scenario with every policy and compares promise and outcome at every action.</summary>
    /// <param name="log">When given, every action of every fight is written to it with what it did (for looking into one fight).</param>
    public static Report Run(IReadOnlyList<Scenario> scenarios, IReadOnlyList<IPolicy> policies, Action<string>? log = null)
    {
        var report = new Report();
        foreach (var scenario in scenarios)
        {
            foreach (var policy in policies)
            {
                try
                {
                    Play(scenario, policy, report, log);
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"{scenario.Id} with {policy.Name} threw {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        return report;
    }

    private static void Play(Scenario scenario, IPolicy policy, Report report, Action<string>? log)
    {
        var fight = new Fight(FightConfig.From(Scenarios.Setup(scenario)));
        var events = new List<Event>();
        fight.DamageTraced = d => events.Add(new Event(null, d));
        fight.HistoryTraced = e => events.Add(new Event(e, null));
        var where = $"{scenario.Id}, {policy.Name}";
        report.Fights++;
        log?.Invoke($"== {where}: {scenario.Summary}");

        // An action that opens a card choice is run again from its start once the choice is
        // answered, so its promise waits here until the action is really through.
        Promise? waiting = null;
        for (var n = 0; n < MostDecisions && !fight.IsOver; n++)
        {
            var snapshot = fight.Snapshot();
            var choice = policy is IEnginePolicy searching ? searching.Choose(snapshot, fight) : policy.Choose(snapshot);
            if (snapshot.Actions.All(a => a.Id != choice))
            {
                report.Refused++;
                report.Unexplained.Add($"{where}, turn {fight.Turn}: the policy chose '{choice}', which is not offered");
                choice = snapshot.Actions.Any(a => a.Id == "end_turn") ? "end_turn" : snapshot.Actions[0].Id;
            }
            report.Decisions++;
            waiting ??= Promise.Note(fight, snapshot, choice, where);

            events.Clear();
            var result = fight.Execute(choice);
            if (!result.Ok)
            {
                report.Refused++;
                report.Unexplained.Add($"{where}, turn {fight.Turn}: '{choice}' was offered and then refused: {result.Error}");
                return;
            }
            if (log is not null)
            {
                log($"turn {fight.Turn} {snapshot.Actions.First(a => a.Id == choice).Label} [{choice}] -> HP {fight.Player.Hp}, Block {fight.Player.Block}, energy {fight.Energy}; " +
                    string.Join(" | ", fight.Roster.Where(fight.InFight).Select(e => $"{e.Name} {e.Hp}/{e.MaxHp} [{string.Join(", ", fight.IntentTexts(e))}]")));
                foreach (var e in events) log("    " + Describe(fight, e));
            }
            if (fight.HasPendingChoice) continue;
            waiting?.Judge(fight, events, report);
            waiting = null;
        }
        if (!fight.IsOver) report.Unexplained.Add($"{where}: the fight did not end in {MostDecisions} decisions");
        if (fight.TimedOut) report.TurnLimit.Add($"{where} ({scenario.Summary})");
        if (fight.Won) report.Won++;
    }

    private static string Describe(Fight fight, Event e)
    {
        if (e.Damage is { } d)
            return string.Create(CultureInfo.InvariantCulture, $"damage {d.Modified} to {NameOf(fight, d.TargetId)} by {(d.DealerId < 0 ? "nobody" : NameOf(fight, d.DealerId))}{(d.CardId != 0 ? $" (card #{d.CardId})" : "")} [{d.Props}]");
        var h = e.Entry!.Value;
        var who = h.Kind is HistoryKind.DamageReceived or HistoryKind.BlockGained or HistoryKind.PowerReceived or HistoryKind.MonsterPerformedMove or HistoryKind.CreatureAttacked
            ? " " + NameOf(fight, h.ActorId) : "";
        return string.Create(CultureInfo.InvariantCulture, $"{h.Kind}{who} {h.Name ?? h.Card?.ClassName ?? ""} amount {h.Amount}{(h.Blocked != 0 ? $" blocked {h.Blocked}" : "")}{(h.CardId != 0 ? $" card #{h.CardId}" : "")} ({h.Side})");
    }

    private static string NameOf(Fight fight, int creatureId) =>
        creatureId == PlayerId ? "the player" : fight.Roster.FirstOrDefault(c => c.Id == creatureId)?.Name ?? $"creature {creatureId}";

    // ---------- reading numbers out of a text ----------

    [GeneratedRegex(@"^Deal (\d+) damage\b", RegexOptions.CultureInvariant)]
    private static partial Regex DealPattern();

    [GeneratedRegex(@"^Deal damage\b[^(]*\((\d+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex WorkedOutDealPattern();

    [GeneratedRegex(@"^If [^,]*, deal (\d+) damage\b", RegexOptions.CultureInvariant)]
    private static partial Regex ConditionalDealPattern();

    [GeneratedRegex(@"^Gain (\d+) Block\b", RegexOptions.CultureInvariant)]
    private static partial Regex GainBlockPattern();

    [GeneratedRegex(@"\b(\d+|X) times\b", RegexOptions.CultureInvariant)]
    private static partial Regex TimesPattern();

    /// <summary>What a text promises when the card or potion is used now: the numbers of the sentences
    /// that begin with "Deal ..." and "Gain N Block". A count is null when the text makes it depend on
    /// something else ("for each", "twice if"), and then only the size of each hit or gain is held to.</summary>
    private readonly record struct Numbers(int? Damage, int? Hits, bool HitsAreX, int? Block, int? BlockGains)
    {
        public static Numbers Read(string text)
        {
            int? damage = null, hits = null, block = null, gains = null;
            var hitsAreX = false;
            var sentences = text.Split([" / ", ". "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var sentence in sentences)
            {
                var deal = DealPattern().Match(sentence);
                if (!deal.Success) deal = WorkedOutDealPattern().Match(sentence);
                if (damage is null && deal.Success)
                {
                    damage = int.Parse(deal.Groups[1].Value, CultureInfo.InvariantCulture);
                    hits = 1;
                    if (sentence.Contains("for each", StringComparison.OrdinalIgnoreCase) && !sentence.Contains('(')) hits = null;
                    else if (sentence.Contains("once, plus", StringComparison.OrdinalIgnoreCase)) hits = null;
                    else if (sentence.Contains("twice", StringComparison.OrdinalIgnoreCase)) hits = 2;
                    else if (TimesPattern().Match(sentence) is { Success: true } times)
                    {
                        if (times.Groups[1].Value == "X")
                        {
                            hitsAreX = true;
                            hits = null;
                        }
                        else hits = int.Parse(times.Groups[1].Value, CultureInfo.InvariantCulture);
                    }
                    continue;
                }
                // "If ..., deal N damage": the size is promised, whether it happens is not.
                if (damage is null && ConditionalDealPattern().Match(sentence) is { Success: true } maybe)
                {
                    damage = int.Parse(maybe.Groups[1].Value, CultureInfo.InvariantCulture);
                    hits = null;
                    continue;
                }
                var gain = GainBlockPattern().Match(sentence);
                if (block is null && gain.Success)
                {
                    block = int.Parse(gain.Groups[1].Value, CultureInfo.InvariantCulture);
                    gains = sentence.Contains("for each", StringComparison.OrdinalIgnoreCase) ? null : 1;
                    continue;
                }
                // A later sentence that changes the count: "If the enemy is Vulnerable, hit it twice", "... gain 8 Block again".
                if (damage is not null && (sentence.Contains("twice", StringComparison.OrdinalIgnoreCase) || TimesPattern().IsMatch(sentence))) hits = null;
                if (block is not null && sentence.Contains("Block again", StringComparison.OrdinalIgnoreCase)) gains = null;
            }
            return new Numbers(damage, hits, hitsAreX, block, gains);
        }
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    private static string Join(IEnumerable<int> numbers) => string.Join(",", numbers);

    // ---------- promises ----------

    private abstract class Promise(string where, int turn)
    {
        protected string Where { get; } = where;
        protected int Turn { get; } = turn;

        public abstract void Judge(Fight after, List<Event> events, Report report);

        public static Promise? Note(Fight fight, GameSnapshot snapshot, string choice, string where)
        {
            if (snapshot.Screen != "combat") return null;
            if (choice == "end_turn") return new TurnPromise(fight, snapshot, where);
            if (choice.StartsWith("play:", StringComparison.Ordinal)) return new CardPromise(fight, snapshot, choice, where);
            if (choice.StartsWith("potion:", StringComparison.Ordinal)) return new PotionPromise(fight, snapshot, choice, where);
            return null;
        }

        /// <summary>
        /// The powers that landed or changed on the given creatures before the event at
        /// <paramref name="before"/>, as "Vulnerable on the player": what can have changed a number
        /// between the snapshot and that moment. Changes after it are no excuse.
        /// </summary>
        protected static string PowerChangesBefore(List<Event> events, int before, params int[] creatureIds)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < before && i < events.Count; i++)
            {
                if (events[i].Entry is not { Kind: HistoryKind.PowerReceived } e || e.Amount == 0 || !creatureIds.Contains(e.ActorId)) continue;
                var power = (e.Name ?? "?").Replace("Power", "", StringComparison.Ordinal);
                names.Add(e.ActorId == PlayerId ? $"{power} on the player" : $"{power} on the enemy");
            }
            return string.Join(", ", names);
        }
    }

    // ---------- "end_turn": intents and incoming damage ----------

    private sealed class TurnPromise : Promise
    {
        /// <param name="MoveId">The move the intents belong to: the one the monster was going to make when the snapshot was taken.</param>
        private sealed record Shown(int RosterIndex, int CreatureId, string Name, string? MoveId, List<string> Intents, List<int> Hits);

        private readonly List<Shown> _enemies = new();
        private readonly int _incoming;
        private readonly int _rosterCount;
        private readonly Dictionary<int, int> _hpBefore = new();

        public TurnPromise(Fight fight, GameSnapshot snapshot, string where) : base(where, fight.Turn)
        {
            _incoming = Int(snapshot.State["incoming_damage"]);
            _rosterCount = fight.Roster.Count;
            foreach (var node in snapshot.State["enemies"] as JsonArray ?? [])
            {
                if (node is not JsonObject enemy) continue;
                var j = Int(enemy["i"]);
                var intents = (enemy["intents"] as JsonArray ?? []).Select(i => Str(i) ?? "").ToList();
                var hits = new List<int>();
                foreach (var intent in intents)
                {
                    if (!intent.StartsWith("attack ", StringComparison.Ordinal)) continue;
                    var parts = intent["attack ".Length..].Split('x');
                    var perHit = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    var count = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
                    hits.AddRange(Enumerable.Repeat(perHit, count));
                }
                _enemies.Add(new Shown(j, fight.Roster[j].Id, fight.Roster[j].Name, fight.Roster[j].Monster?.NextMove?.Id, intents, hits));
                _hpBefore[fight.Roster[j].Id] = fight.Roster[j].Hp;
            }
        }

        public override void Judge(Fight after, List<Event> events, Report report)
        {
            report.EnemyTurns++;
            var playerDied = after.IsOver && !after.Won && !after.TimedOut;

            // What each monster did in its own move: the entries of the enemy side up to its "performed" entry.
            var segments = new Dictionary<int, (string Move, List<HistoryEntry> Entries)>();
            var open = new List<HistoryEntry>();
            foreach (var ev in events)
            {
                if (ev.Entry is not { Side: Side.Enemy } e) continue;
                if (e.Kind == HistoryKind.MonsterPerformedMove)
                {
                    segments[e.ActorId] = (e.Name ?? "", open);
                    open = new List<HistoryEntry>();
                }
                else open.Add(e);
            }

            var actualTotal = 0;
            var allKept = true;
            foreach (var shown in _enemies)
            {
                // Powered hits of this enemy on the player, in order, as the damage chain left them.
                var hitAt = Enumerable.Range(0, events.Count)
                    .Where(i => events[i].Damage is { } t && t.DealerId == shown.CreatureId && t.TargetId == PlayerId && t.Props.IsPoweredAttack()).ToList();
                var actual = hitAt.Select(i => (int)events[i].Damage!.Value.Modified).ToList();
                actualTotal += actual.Sum();
                var creature = after.Roster[shown.RosterIndex];
                var performed = segments.TryGetValue(shown.CreatureId, out var segment);
                var example = $"{Where}, turn {Turn}: {shown.Name} showed [{string.Join(", ", shown.Intents)}], hit for [{Join(actual)}]";

                // It made another move than the one it showed: something replaced its move after the snapshot.
                // The only thing that can is damage it took meanwhile (the player's end-of-turn effects, an
                // earlier enemy's reaction): a stun at an HP threshold, or 0 HP and a turn spent coming back.
                if (performed && segment.Move != shown.MoveId)
                {
                    var performedAt = events.FindIndex(e => e.Entry is { Kind: HistoryKind.MonsterPerformedMove } m && m.ActorId == shown.CreatureId);
                    var wasHit = events.Take(performedAt).Any(e => e.Damage is { } d && d.TargetId == shown.CreatureId);
                    report.IntentAttacks += shown.Hits.Count > 0 ? 1 : 0;
                    report.OtherIntents += shown.Intents.Count(i => !i.StartsWith("attack ", StringComparison.Ordinal));
                    allKept = false;
                    if (!wasHit) report.Unexplained.Add($"{example}: it performed {segment.Move}, not the {shown.MoveId} it showed, and nothing had hit it");
                    else if (segment.Move == Monster.StunnedId)
                        report.Explain("intent not carried out: the enemy was stunned after the snapshot (damage from an end-of-turn effect of the player's took it past its threshold) and lost its move", example);
                    else
                        report.Explain($"intent not carried out: the enemy's move was replaced after the snapshot by {segment.Move} (an end-of-turn effect of the player's brought it to 0 HP, and it spent its turn coming back)", example);
                    continue;
                }

                if (shown.Hits.Count > 0)
                {
                    report.IntentAttacks++;
                    if (actual.SequenceEqual(shown.Hits)) report.IntentAttacksKept++;
                    else
                    {
                        allKept = false;
                        Classify(after, report, shown, actual, hitAt, performed ? segment.Move : null, events, creature, playerDied, example);
                    }
                }
                else if (actual.Count > 0)
                {
                    allKept = false;
                    report.Unexplained.Add($"{example}: it attacked without showing an attack");
                }

                // The other intents: did the move do anything of the kind it announced?
                foreach (var intent in shown.Intents.Where(i => !i.StartsWith("attack ", StringComparison.Ordinal)))
                {
                    report.OtherIntents++;
                    var cause = OtherIntent(after, shown, intent, performed, performed ? segment : default, creature, playerDied);
                    if (cause is null) report.OtherIntentsKept++;
                    else if (cause.StartsWith('!')) report.Unexplained.Add($"{Where}, turn {Turn}: {shown.Name} showed '{intent}': {cause[1..]}");
                    else report.Explain(cause, $"{Where}, turn {Turn}: {shown.Name} showed '{intent}'");
                }
            }

            // A monster that was not in the snapshot (it joined during this turn) must not have attacked.
            foreach (var ev in events)
                if (ev.Damage is { } t && t.TargetId == PlayerId && t.DealerId > PlayerId && t.Props.IsPoweredAttack() && _enemies.All(s => s.CreatureId != t.DealerId))
                    report.Unexplained.Add($"{Where}, turn {Turn}: {NameOf(after, t.DealerId)} hit for {(int)t.Modified} without having been shown");

            report.IncomingTotals++;
            if (actualTotal == _incoming) report.IncomingTotalsKept++;
            else if (allKept) report.Unexplained.Add($"{Where}, turn {Turn}: incoming_damage said {_incoming}, the hits came to {actualTotal}, and every single intent was kept");
        }

        private void Classify(Fight after, Report report, Shown shown, List<int> actual, List<int> hitAt, string? move, List<Event> events,
            Creature creature, bool playerDied, string example)
        {
            var gone = creature.IsDead || !after.Enemies.Contains(creature);
            if (move is null)
            {
                if (after.Won) report.Explain("attack intent not carried out: the fight was won before the enemy acted (an end-of-turn effect of the player's killed the last enemy)", example);
                else if (playerDied) report.Explain("attack intent not carried out: the player died earlier in the enemy turn", example);
                else if (gone) report.Explain("attack intent not carried out: the enemy died before it acted (an end-of-turn effect of the player's, or a reaction to an earlier enemy's hit)", example);
                else report.Unexplained.Add($"{example}: it did not perform any move");
                return;
            }
            if (move == Monster.StunnedId)
            {
                report.Explain("attack intent not carried out: the enemy was stunned after the snapshot (by an end-of-turn effect of the player's) and lost its move", example);
                return;
            }

            // Fewer hits than shown, the ones that came as shown: the attack was cut short.
            var prefix = actual.Count < shown.Hits.Count && actual.SequenceEqual(shown.Hits.Take(actual.Count));
            if (prefix && playerDied)
            {
                report.Explain("attack cut short: the player died before the last hit", example);
                return;
            }
            if (prefix && gone)
            {
                report.Explain("attack cut short: the attacker died in the middle of its own attack (the player's Thorns, Flame Barrier or the like)", example);
                return;
            }

            // The hits came, with other numbers: a modifier changed after the snapshot and before the first hit that differs.
            if (actual.Count == shown.Hits.Count)
            {
                var firstOff = Enumerable.Range(0, actual.Count).First(k => actual[k] != shown.Hits[k]);
                var changes = PowerChangesBefore(events, hitAt[firstOff], PlayerId, shown.CreatureId);
                if (changes.Length > 0)
                {
                    report.Explain($"hit differs from the intent shown: a modifier changed between the snapshot and the hit ({changes})", example);
                    return;
                }
            }
            report.Unexplained.Add(example);
        }

        /// <summary>Null when the intent was kept, a cause when it was not; a cause that starts with '!' is no known cause.</summary>
        private string? OtherIntent(Fight after, Shown shown, string intent, bool performed, (string Move, List<HistoryEntry> Entries) segment,
            Creature creature, bool playerDied)
        {
            if (!performed || segment.Move == Monster.StunnedId)
            {
                if (intent == "stunned" && performed) return null;
                if (after.Won) return "intent not carried out: the fight was won before the enemy acted";
                if (playerDied) return "intent not carried out: the player died earlier in the enemy turn";
                if (creature.IsDead || !after.Enemies.Contains(creature)) return "intent not carried out: the enemy died before it acted";
                if (performed) return "intent not carried out: the enemy was stunned after the snapshot and lost its move";
                return "!it did not perform any move";
            }
            var did = segment.Entries;
            var ended = creature.IsDead || playerDied || after.Won;
            switch (intent)
            {
                case "defend":
                    if (did.Any(e => e.Kind == HistoryKind.BlockGained && e.Amount > 0)) return null;
                    return ended ? "defend intent without Block: the fight or the enemy ended first" : $"!its move {segment.Move} gave nobody Block";
                case "buff":
                    if (did.Any(e => e.Kind == HistoryKind.PowerReceived && e.ActorId != PlayerId && e.Amount != 0)) return null;
                    return ended ? "buff intent without a buff: the fight or the enemy ended first" : $"!its move {segment.Move} gave no enemy a power";
                case "debuff" or "debuffstrong" or "carddebuff":
                    if (did.Any(e => e.Kind == HistoryKind.PowerReceived && e.ActorId == PlayerId && e.Amount != 0)) return null;
                    if (did.Any(e => e.Kind == HistoryKind.CardGenerated)) return null;
                    return ended ? "debuff intent without a debuff: the fight or the enemy ended first" : $"!its move {segment.Move} put no power on the player";
                case "summon":
                    return after.Roster.Count > _rosterCount || ended ? null : $"!its move {segment.Move} summoned nobody";
                case "heal":
                    return creature.Hp > _hpBefore.GetValueOrDefault(shown.CreatureId) || ended ? null : $"!its move {segment.Move} healed nobody";
                case "sleep" or "stun" or "stunned" or "hidden" or "unknown" or "escape":
                    return null;
                default:
                    if (intent.StartsWith("statuscard: ", StringComparison.Ordinal))
                    {
                        var want = int.Parse(intent["statuscard: ".Length..], CultureInfo.InvariantCulture);
                        var got = did.Count(e => e.Kind == HistoryKind.CardGenerated);
                        if (got == want) return null;
                        return playerDied || after.Won ? "status cards not added: the fight ended first" : $"!its move {segment.Move} added {got} cards";
                    }
                    return $"!the check does not know the intent '{intent}'";
            }
        }
    }

    // ---------- a card ----------

    private sealed class CardPromise : Promise
    {
        private readonly int _cardId;
        private readonly string _title;
        private readonly TargetType _target;
        private readonly int _aimedAt = -1;
        private readonly string _costText;
        private readonly int _energy;
        private readonly string _text;
        private readonly Dictionary<int, Numbers> _perEnemy = new();
        private readonly Numbers _atRest;
        private readonly int _rosterCount;
        private readonly HashSet<string> _playerPowers;

        public CardPromise(Fight fight, GameSnapshot snapshot, string choice, string where) : base(where, fight.Turn)
        {
            _playerPowers = fight.Player.Powers.Select(p => p.ClassName).ToHashSet(StringComparer.Ordinal);
            var arrow = choice.IndexOf('>');
            var index = int.Parse(arrow < 0 ? choice.AsSpan(5) : choice.AsSpan(5, arrow - 5), NumberStyles.None, CultureInfo.InvariantCulture);
            var card = fight.Hand[index];
            var json = (snapshot.State["hand"] as JsonArray)![index] as JsonObject ?? new JsonObject();
            _cardId = card.Id;
            _title = card.Title;
            _target = card.Target;
            _costText = Str(json["cost"]) ?? "";
            _energy = fight.Energy;
            _text = Str(json["text"]) ?? "";
            _atRest = Numbers.Read(_text);
            _rosterCount = fight.Roster.Count;
            if (arrow >= 0) _aimedAt = fight.Roster[int.Parse(choice.AsSpan(arrow + 2), NumberStyles.None, CultureInfo.InvariantCulture)].Id;

            // Against each enemy the card can reach: its "vs" text where the snapshot gives one, else the text at rest.
            foreach (var node in snapshot.State["enemies"] as JsonArray ?? [])
            {
                if (node is not JsonObject enemy) continue;
                var j = Int(enemy["i"]);
                var versus = Str((json["vs"] as JsonObject)?[$"e{j}"]);
                _perEnemy[fight.Roster[j].Id] = versus is null ? _atRest : Numbers.Read(versus);
            }
        }

        public override void Judge(Fight after, List<Event> events, Report report)
        {
            report.CardPlays++;
            var ended = after.IsOver;
            var example = $"{Where}, turn {Turn}: {_title} \"{_text}\"";

            // Where each run of the card's effect starts (a doubled card runs twice), and the runs' ids.
            var runStarts = Enumerable.Range(0, events.Count).Where(i => events[i].Entry is { Kind: HistoryKind.CardPlayStarted } e && e.CardId == _cardId).ToList();
            var runIds = runStarts.Select(i => events[i].Entry!.Value.PlayId).ToList();
            var runCount = Math.Max(1, runStarts.Count);
            int RunOf(int eventIndex) => runStarts.Count(s => s <= eventIndex);

            // ----- the cost -----
            var spent = events.FirstOrDefault(e => e.IsKind(HistoryKind.EnergySpent)).Entry?.Amount ?? 0;
            var promised = _costText == "X" ? _energy : int.TryParse(_costText, NumberStyles.None, CultureInfo.InvariantCulture, out var c) ? c : 0;
            report.CardCosts++;
            if (spent == promised) report.CardCostsKept++;
            else report.Unexplained.Add($"{example}: cost shown {_costText}, energy paid {spent}");

            // ----- the damage -----
            var hitAt = Enumerable.Range(0, events.Count).Where(i => events[i].Damage is { } t && t.CardId == _cardId && t.DealerId == PlayerId && t.TargetId != PlayerId).ToList();
            if (_atRest.Damage is not null || hitAt.Count > 0)
            {
                report.CardDamage++;
                if (JudgeDamage(after, report, events, hitAt, runCount, RunOf, ended, example)) report.CardDamageKept++;
            }

            // ----- the Block -----
            if (_atRest.Block is { } block)
            {
                var gainAt = Enumerable.Range(0, events.Count)
                    .Where(i => events[i].Entry is { Kind: HistoryKind.BlockGained } e && e.ActorId == PlayerId && e.CardId == _cardId && runIds.Contains(e.PlayId)).ToList();
                var gains = gainAt.Select(i => events[i].Entry!.Value.Amount).ToList();
                report.CardBlock++;
                var sizeKept = gains.All(g => g == block);
                var countKept = _atRest.BlockGains is not { } perRun || gains.Count == perRun * runCount;
                var said = $"{example}: gained [{Join(gains)}]";
                if (sizeKept && countKept) report.CardBlockKept++;
                else if (after.Player.HasPower("UnmovablePower") && gains.Count > 0 && gains[0] == block * 2 && gains.Skip(1).All(g => g == block))
                    report.Explain("Block gained differs from the card's number: Unmovable doubled the turn's first Block card, which the card text does not show", said);
                else if (!countKept && sizeKept && ended)
                    report.Explain("Block not gained: the fight ended during the card", said);
                else if (gains.Count > 0 && PowerChangesBefore(events, gainAt[Enumerable.Range(0, gains.Count).First(k => gains[k] != block)], PlayerId) is { Length: > 0 } changes)
                    report.Explain($"Block gained differs from the card's number: the card (or a reaction to it) changed a modifier before the Block ({changes})", said);
                else report.Unexplained.Add($"{said}: Block shown {block}");
            }
        }

        /// <summary>True when every hit was as shown; otherwise the difference is filed and false returned.</summary>
        private bool JudgeDamage(Fight after, Report report, List<Event> events, List<int> hitAt, int runCount, Func<int, int> runOf, bool ended, string example)
        {
            var hits = hitAt.Select(i => events[i].Damage!.Value).ToList();
            var targets = hits.Select(t => t.TargetId).Distinct().ToList();
            var said = $"{example}: hits [{string.Join("; ", targets.Select(id => $"{NameOf(after, id)} {Join(hits.Where(t => t.TargetId == id).Select(t => (int)t.Modified))}"))}]";

            if (_atRest.Damage is null)
            {
                report.Unexplained.Add($"{said}: the text names no damage");
                return false;
            }

            // A card that picks its own targets is shown without one: its hits are held to the count, and each to the number at rest.
            if (_target == TargetType.RandomEnemy)
            {
                var expected = _atRest.Hits is { } h ? h * runCount : (int?)null;
                var countKept = expected is null || hits.Count == expected || ended || !after.Enemies.Any(e => e.IsAlive);
                if (!countKept)
                {
                    report.Unexplained.Add($"{said}: {expected} hits were shown");
                    return false;
                }
                if (hits.All(t => (int)t.Modified == _atRest.Damage)) return true;
                report.Explain("hit differs from the card's number: a card that picks its own target at random is shown without a target (no \"vs\"), so what the enemy it lands on adds (Vulnerable and the like) is not in the number", said);
                return false;
            }

            var kept = true;
            var aimed = _target == TargetType.AnyEnemy ? new List<int> { _aimedAt } : _perEnemy.Keys.ToList();
            foreach (var stray in targets.Where(id => !aimed.Contains(id)))
            {
                kept = false;
                var index = after.Roster.FirstOrDefault(c => c.Id == stray)?.RosterIndex ?? -1;
                if (_target == TargetType.AllEnemies && index >= _rosterCount)
                    report.Explain("an all-enemies attack of several hits also hit enemies that joined the fight in the middle of it (the Phrog Parasite's Wrigglers): every hit takes everyone alive at that moment", said);
                else report.Unexplained.Add($"{said}: {NameOf(after, stray)} was hit, which the card was not shown against");
            }
            foreach (var id in aimed)
            {
                var numbers = _perEnemy.GetValueOrDefault(id, _atRest);
                var landedAt = hitAt.Where(i => events[i].Damage!.Value.TargetId == id).ToList();
                var landed = landedAt.Select(i => (int)events[i].Damage!.Value.Modified).ToList();
                var perRun = numbers.HitsAreX ? _energy : numbers.Hits;
                var expected = perRun is { } p ? p * runCount : (int?)null;
                var sizeKept = numbers.Damage is { } size && landed.All(d => d == size);
                var countKept = expected is null || landed.Count == expected;
                if (sizeKept && countKept) continue;
                kept = false;

                var target = after.Roster.FirstOrDefault(c => c.Id == id);
                var gone = target is null || target.IsDead || !after.Enemies.Contains(target);
                if (sizeKept && landed.Count < expected && (gone || ended))
                {
                    report.Explain("fewer hits than the card shows: the target died (or the fight ended) before the last hit", said);
                    continue;
                }
                if (sizeKept && landed.Count == 0 && _target == TargetType.AllEnemies && target is not null && !after.IsHittable(target))
                {
                    report.Explain("an enemy was not hit by an all-enemies card: it could not be hit at that moment", said);
                    continue;
                }
                if (!sizeKept)
                {
                    // The first hit that is not as shown: in a later run of a card played more than once, or in its first.
                    var firstOff = landedAt[Enumerable.Range(0, landed.Count).First(k => landed[k] != numbers.Damage)];
                    if (runOf(firstOff) > 1)
                    {
                        report.Explain("hit differs from the card's number: the card was played more than once (Duplication, One-Two Punch, a replay), and a later run met what the earlier run had changed (Vulnerable it applied, Strength it gave, its own growth, a spent tripling, the Slow count)", said);
                        continue;
                    }
                    if (PowerChangesBefore(events, firstOff, PlayerId, id) is { Length: > 0 } changes)
                    {
                        report.Explain($"hit differs from the card's number: the card (or a reaction to it) changed a modifier before the hit ({changes})", said);
                        continue;
                    }
                    // A power that ends with the enemy that applied it (the Shrinker Beetle's Shrink): the card
                    // killed that enemy with an earlier hit, and the hits after it are no longer shrunk.
                    var killedBefore = events.Take(firstOff).Any(e => e.Entry is { Kind: HistoryKind.DamageReceived, WasKilled: true } k && k.ActorId != PlayerId);
                    var lost = _playerPowers.Where(name => !after.Player.HasPower(name)).Select(name => name.Replace("Power", "", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
                    if (killedBefore && lost.Count > 0)
                    {
                        report.Explain($"hit differs from the card's number: an earlier hit of the same card killed an enemy, and a power on the player ended with it ({string.Join(", ", lost)})", said);
                        continue;
                    }
                }
                report.Unexplained.Add($"{said}: shown {numbers.Damage} x {(expected is null ? "?" : expected.ToString())} against {NameOf(after, id)}");
            }
            return kept;
        }
    }

    // ---------- a potion ----------

    private sealed class PotionPromise : Promise
    {
        private readonly string _name;
        private readonly string _text;
        private readonly Numbers _numbers;
        private readonly int _blockBefore;

        public PotionPromise(Fight fight, GameSnapshot snapshot, string choice, string where) : base(where, fight.Turn)
        {
            var arrow = choice.IndexOf('>');
            var slot = int.Parse(arrow < 0 ? choice.AsSpan(7) : choice.AsSpan(7, arrow - 7), NumberStyles.None, CultureInfo.InvariantCulture);
            var json = (snapshot.State["potions"] as JsonArray)?[slot] as JsonObject ?? new JsonObject();
            _name = Str(json["name"]) ?? "?";
            _text = Str(json["text"]) ?? "";
            _numbers = Numbers.Read(_text);
            _blockBefore = fight.Player.Block;
        }

        public override void Judge(Fight after, List<Event> events, Report report)
        {
            report.PotionUses++;
            var example = $"{Where}, turn {Turn}: {_name} \"{_text}\"";
            if (_numbers.Damage is { } damage)
            {
                // The potion's own damage: dealt by the player with no card behind it, unpowered.
                var hits = events.Where(e => e.Damage is { } t && t.DealerId == PlayerId && t.CardId == 0 && t.TargetId != PlayerId && !t.Props.IsPoweredAttack())
                    .Select(e => (int)e.Damage!.Value.Modified).ToList();
                report.PotionNumbers++;
                if (hits.Count > 0 && hits.All(h => h == damage)) report.PotionNumbersKept++;
                else report.Unexplained.Add($"{example}: hits [{Join(hits)}]");
            }
            if (_numbers.Block is { } block && !after.IsOver)
            {
                report.PotionNumbers++;
                if (after.Player.Block - _blockBefore >= block) report.PotionNumbersKept++;
                else report.Unexplained.Add($"{example}: Block went from {_blockBefore} to {after.Player.Block}");
            }
        }
    }
}
