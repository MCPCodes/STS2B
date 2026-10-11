using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpireMind.Core;

/// <summary>
/// Co-op fights: teammates play in the same window as the bot, so a turn planned from one
/// snapshot can go stale while it runs (Mark gave the bot Block and it played its Defends
/// anyway). act_sequence looks before each step for changes its own steps don't explain, to
/// the numbers a plan rests on: the bot's Block, energy, HP and powers, the planned targets'
/// HP, Block and powers, which enemies are still there, and incoming_damage. The game side's
/// own record of what the others did (state.team_since and team_changed, docs/coop.md) is a
/// second, more precise sign: a line the model hasn't seen, in a snapshot that says a line
/// touched the bot, its pet or an enemy, when no line the model knew can have set that. On
/// either it stops and hands back the fresh snapshot to plan from. A lone act or a sequence sent on an older snapshot runs when only teammates
/// changed things since and neither sign shows. None of this runs in a solo fight.
/// </summary>
public sealed partial class BotTools
{
    /// <summary>Co-op fight snapshots handed to the model, by version, for an act sent on one
    /// that teammates have since moved past (<see cref="PlanFrom"/>).</summary>
    private readonly Dictionary<long, GameSnapshot> _handedOut = new();
    private readonly Queue<long> _handedOrder = new();
    private const int HandedOutKept = 16;

    /// <summary>The version the bot's own last action settled into, or long.MaxValue while one
    /// runs. A snapshot older than it was planned before that action, so it can't be used.</summary>
    private long _ownActionMark;

    /// <summary>Whether a snapshot is a fight with other players in it: the state names teammates
    /// ("team" in a co-op run, "teammates" when that list couldn't be read).</summary>
    internal static bool IsCoopFight(GameSnapshot s) => s.Screen is "combat" or HandSelect && HasTeammates(s);

    private static bool HasTeammates(GameSnapshot s) =>
        s.State["team"] is JsonArray { Count: > 0 } || s.State["teammates"] is JsonArray { Count: > 0 };

    private void Remember(GameSnapshot snap)
    {
        if (!IsCoopFight(snap))
            return;
        lock (_handedOut)
        {
            if (_handedOut.ContainsKey(snap.Version))
                return;
            _handedOut[snap.Version] = snap;
            _handedOrder.Enqueue(snap.Version);
            while (_handedOrder.Count > HandedOutKept)
                _handedOut.Remove(_handedOrder.Dequeue());
        }
    }

    /// <summary>
    /// The snapshot the model's ids came from: <paramref name="current"/> when version is its
    /// version. Otherwise, in a co-op fight, the older snapshot it was handed, as long as the bot
    /// has done nothing since, it is still the same turn and screen with the same hand, the
    /// record shows no new line that touched anything (<see cref="TeamNews"/>), and what changed
    /// is nothing the plan rests on (<see cref="TeammateChanges"/>). Plan is null when nothing
    /// may run, and Why says why.
    /// </summary>
    private (GameSnapshot? Plan, string? Why) PlanFrom(long version, GameSnapshot current, IEnumerable<string> ids)
    {
        if (CannotAct(current, version) is not { } why)
            return (current, null);
        if (version >= current.Version || _chat.PausedBy is not null || !current.AwaitingDecision || !IsCoopFight(current))
            return (null, why);
        if (version < Interlocked.Read(ref _ownActionMark))
            return (null, why);
        GameSnapshot? older;
        lock (_handedOut)
            _handedOut.TryGetValue(version, out older);
        if (older is null || older.Screen != current.Screen || !SameTurn(older, current)
            || !JsonNode.DeepEquals(older.State["selection"], current.State["selection"]))
            return (null, why);
        // The bot did nothing since, so a different hand is a teammate's doing (Huddle Up).
        if (!SameHand(older, current))
            return (null, "A teammate acted since that snapshot: your hand changed. Nothing was done; plan from the current snapshot.");
        if (TeamNews(TeamRecord.Of(older), TeamRecord.Of(current), mayBeCleared: false) is { } lines)
            return (null, $"A teammate acted since that snapshot: {lines}. Nothing was done; plan from the current snapshot.");
        if (TeammateChanges(older, current, own: null, Targets(ids)) is { } changes)
            return (null, $"A teammate acted since that snapshot: {changes}. Nothing was done; plan from the current snapshot.");
        return (older, null);
    }

    /// <summary>Whether two snapshots hold the same cards in the same places, by name, cost and
    /// type, leaving out text that a power anywhere on the board rewrites.</summary>
    private static bool SameHand(GameSnapshot a, GameSnapshot b)
    {
        var ha = a.State["hand"] as JsonArray;
        var hb = b.State["hand"] as JsonArray;
        if (ha is null || hb is null || ha.Count != hb.Count)
            return ha is null && hb is null;
        for (var i = 0; i < ha.Count; i++)
            if (!JsonNode.DeepEquals(HandEntry(a, $"play:{i}", withText: false), HandEntry(b, $"play:{i}", withText: false)))
                return false;
        return true;
    }

    /// <summary>The enemy indices the ids aim at, 0 of "play:2>e0".</summary>
    private static HashSet<int> Targets(IEnumerable<string> ids)
    {
        var targets = new HashSet<int>();
        foreach (var id in ids)
            if (TargetOf(id) is { } t && t.StartsWith(">e", StringComparison.Ordinal)
                && int.TryParse(t[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var j))
                targets.Add(j);
        return targets;
    }

    /// <summary>
    /// The game right before the next step of a co-op sequence, after <paramref name="last"/>
    /// settled: captured again, since a teammate may have played meanwhile, and waited for when
    /// the game is busy with their card.
    /// </summary>
    private async Task<GameSnapshot> FreshAsync(GameSnapshot last, CancellationToken ct)
    {
        var snap = await _waiter.CaptureAsync(ct);
        if (snap.Version == last.Version || snap.AwaitingDecision)
            return snap;
        return await _waiter.WaitAsync(afterVersion: null, ActSettleTimeout, ct);
    }

    /// <summary>Why the next step can't run on <paramref name="fresh"/>, captured after the last
    /// step settled: the game went busy, the screen or the turn moved on. Null to go on.</summary>
    private static string? Drift(GameSnapshot fresh, GameSnapshot start, bool pickNext)
    {
        if (!fresh.AwaitingDecision)
            return $"The game is still busy ({fresh.Waiting ?? "no decision yet"}). Call wait_for_decision before acting again.";
        if (fresh.Screen != start.Screen && !(pickNext && fresh.Screen == HandSelect))
            return $"The screen changed to {fresh.Screen}.";
        if (!SameTurn(start, fresh))
            return "Your turn ended.";
        return null;
    }

    /// <summary>
    /// state.team_since, what the other players did since the bot's last action (docs/coop.md,
    /// What the others just did): its lines without the "(N earlier lines left out)" one, that N
    /// (0 without it), and whether team_changed is set. No lines and no flag when there are none:
    /// solo, the arena, nothing to tell.
    /// </summary>
    private readonly record struct TeamRecord(int LeftOut, List<string> Lines, bool Changed)
    {
        public static TeamRecord Of(GameSnapshot s)
        {
            var lines = new List<string>();
            var leftOut = 0;
            if (s.State["team_since"] is JsonArray list)
                foreach (var item in list)
                {
                    if (Str(item) is not { Length: > 0 } line)
                        continue;
                    if (LeftOutPattern.Match(line) is { Success: true } m)
                        leftOut = int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
                    else
                        lines.Add(line);
                }
            var changed = s.State["team_changed"] is JsonValue v && v.TryGetValue<bool>(out var c) && c;
            return new TeamRecord(leftOut, lines, changed);
        }
    }

    private static readonly Regex LeftOutPattern = new(@"^\((\d+) earlier lines? left out\)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// What a teammate did that the model hasn't seen and that touched the bot, its pet or an
    /// enemy: the lines of <paramref name="now"/> not in <paramref name="known"/>
    /// (<see cref="Unknown"/>), joined with "; ". Null when there are none, or when now's
    /// team_changed isn't set: lines alone (a teammate buffing themselves) never count. The flag
    /// is for all the lines together, so it speaks for the new ones only when no known line can
    /// have set it: known had no flag, or its lines are gone. Nothing clears lines but the bot's
    /// own action, so they can be gone only when <paramref name="mayBeCleared"/> (the first check
    /// after a sequence's first step); then none of now's lines may match one, nor may any have
    /// slid out of view.
    /// </summary>
    private static string? TeamNews(TeamRecord known, TeamRecord now, bool mayBeCleared)
    {
        if (!now.Changed)
            return null;
        var fresh = Unknown(known, now);
        if (fresh.Count == 0)
            return null;
        var knownLeft = fresh.Count < now.Lines.Count || now.LeftOut > 0;
        if (known.Changed && (!mayBeCleared || knownLeft))
            return null;
        return string.Join("; ", fresh);
    }

    /// <summary>
    /// The lines of <paramref name="now"/> not in <paramref name="known"/>, as multisets of exact
    /// strings: a line twice in now and once in known is new once. In now's order. Known's first
    /// lines that now's larger left-out count says slid out of view are left out of the match,
    /// so a new line with the same text as one of them is still new.
    /// </summary>
    private static List<string> Unknown(TeamRecord known, TeamRecord now)
    {
        var left = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in known.Lines.Skip(Math.Max(0, now.LeftOut - known.LeftOut)))
            left[line] = left.GetValueOrDefault(line) + 1;
        var fresh = new List<string>();
        foreach (var line in now.Lines)
        {
            if (left.GetValueOrDefault(line) is var n and > 0)
                left[line] = n - 1;
            else
                fresh.Add(line);
        }
        return fresh;
    }

    /// <summary>
    /// The team_since lines a co-op act_sequence has already judged. At first those the model
    /// planned with: the snapshot the ids came from, plus the new lines of the capture the first
    /// step ran on when that is newer (<see cref="PlanFrom"/> let them pass). After that, those
    /// of the last snapshot a check passed. A line stays in every snapshot until the bot's own
    /// action clears it, so one judged once is not judged again, while one with the same text
    /// that comes after it was cleared is a new play. The exception is the first check: the first
    /// step clears the lines the model planned with, and a line with the same text as one of them
    /// is taken for it (the numbers still see what it did).
    /// </summary>
    private sealed class TeamSeen
    {
        private TeamRecord _known;
        private bool _first = true;

        public TeamSeen(GameSnapshot start, GameSnapshot first)
        {
            // Nothing cleared between the two (PlanFrom), so first's new lines follow start's.
            var a = TeamRecord.Of(start);
            var b = TeamRecord.Of(first);
            _known = new TeamRecord(a.LeftOut, [.. a.Lines, .. Unknown(a, b)], a.Changed || b.Changed);
        }

        /// <summary>The new lines of <paramref name="now"/> that its team_changed speaks for
        /// (<see cref="TeamNews"/>). Null when none, and now's lines are judged then.</summary>
        public string? Check(GameSnapshot now)
        {
            var record = TeamRecord.Of(now);
            if (TeamNews(_known, record, mayBeCleared: _first) is { } news)
                return news;
            _known = record;
            _first = false;
            return null;
        }
    }

    /// <summary>
    /// What one of the bot's own steps may have changed, worked out from its card: a card that
    /// mentions Block explains a Block gain up to the numbers in its text, one that mentions
    /// energy or (E) an energy gain, healing or HP an HP gain, and so on. A step this can't
    /// read (a potion, a pick with no card behind it) explains everything.
    /// </summary>
    private sealed record OwnStep(string Text, string Type, int? Target, int? Cost, bool Everything)
    {
        public static readonly OwnStep Anything = new("", "", null, null, Everything: true);
        public static readonly OwnStep Nothing = new("", "", null, 0, Everything: false);

        /// <summary>The step <paramref name="step"/> ran, with <paramref name="opener"/> the card
        /// that opened the hand choice a select or confirm answers. A pick goes by that card's
        /// text (Survivor's Block, a power it sets up) but spends nothing: the card was paid for
        /// when it was played.</summary>
        public static OwnStep Of(Step step, OwnStep? opener)
        {
            var id = step.Id;
            if (id.StartsWith("play:", StringComparison.Ordinal))
            {
                if (HandEntry(step.Before, id, withText: true) is not JsonObject card)
                    return Anything;
                var target = TargetOf(id) is { } t && t.StartsWith(">e", StringComparison.Ordinal)
                    && int.TryParse(t[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var j) ? j : (int?)null;
                // "2", or "2+1*" with stars. "X" (or no number) counts as spending nothing, so
                // only a rise in energy can look like a teammate's.
                var cost = Str(card["cost"]) is { } c && LeadingNumberPattern.Match(c) is { Success: true } m
                    ? int.Parse(m.Value, CultureInfo.InvariantCulture) : (int?)null;
                return new OwnStep((Str(card["text"]) ?? "").ToLowerInvariant(), Str(card["type"]) ?? "", target, cost, Everything: false);
            }
            if (id.StartsWith("select:", StringComparison.Ordinal) || id == "confirm")
                return opener is null ? Anything : opener with { Cost = 0 };
            if (id.StartsWith("potion:", StringComparison.Ordinal))
                return Anything;
            return Nothing;
        }

        private bool Says(string word) => Text.Contains(word, StringComparison.Ordinal);

        /// <summary>Whether the step may have touched any enemy: it aims at one, is an attack, or
        /// its text speaks of damage or enemies.</summary>
        public bool TouchesEnemies => Everything || Target is not null || Type == "Attack" || Says("damage") || Says("enem");

        /// <summary>Whether the step may have touched enemy <paramref name="i"/>.</summary>
        public bool Touches(int i) =>
            Everything || (Target is { } t
                ? t == i || Says("enemies") || Says("random")
                : TouchesEnemies);

        /// <summary>The most Block the card's text gives ("Gain 5 Block"), 0 when it doesn't speak
        /// of Block, or null when it does without a number.</summary>
        public int? BlockGain()
        {
            if (Everything)
                return null;
            if (!Says("block"))
                return 0;
            var numbers = BlockNumberPattern.Matches(Text);
            return numbers.Count == 0 ? null : numbers.Sum(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        public bool MayGainEnergy => Everything || Says("(e)") || Says("energy");

        public bool MayHeal => Everything || Says("heal") || Says("hp");

        public bool ExplainsPower(string name) => Everything || Type == "Power" || Says(name.ToLowerInvariant());
    }

    private static readonly Regex BlockNumberPattern = new(@"(\d+)\s+block", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingNumberPattern = new(@"^\d+", RegexOptions.CultureInvariant);

    /// <summary>
    /// What changed from <paramref name="was"/> to <paramref name="now"/> that the bot's own step
    /// in between (<paramref name="own"/>, null when it took none) doesn't explain, as a short
    /// list ("your Block 0 -> 11"), or null when nothing did. Only gains count on the bot's side
    /// (a teammate gives, an own card spends), and powers that react to plays ("Whenever...")
    /// are left out, since own cards set them off. <paramref name="targets"/> are the enemies
    /// the rest of the plan aims at.
    /// </summary>
    private static string? TeammateChanges(GameSnapshot was, GameSnapshot now, OwnStep? own, IReadOnlySet<int> targets)
    {
        if (own is { Everything: true })
            return null;
        var changes = new List<string>();
        var meWas = was.State["me"];
        var meNow = now.State["me"];
        var myPowersWas = Powers(meWas);
        var myPowersNow = Powers(meNow);

        // Block. A power of the bot's that speaks of Block (After Image) may add some on any card.
        var blockWas = Int(meWas?["block"]) ?? 0;
        var blockNow = Int(meNow?["block"]) ?? 0;
        if (blockNow > blockWas)
        {
            var slack = myPowersWas.Values.Where(p => p.Text.Contains("block", StringComparison.Ordinal)).Sum(p => Math.Max(1, p.Amount));
            var allowed = own is null ? 0 : own.BlockGain();
            if (allowed is { } max && blockNow - blockWas > max + slack)
                changes.Add($"your Block {blockWas} -> {blockNow}");
        }

        // Energy: the card spends its cost, so a teammate's gift can hide in a smaller drop.
        if (Int(was.State["energy"]) is { } energyWas && Int(now.State["energy"]) is { } energyNow
            && energyNow > energyWas - (own?.Cost ?? 0) && own?.MayGainEnergy != true
            && !myPowersWas.Values.Any(p => p.Text.Contains("(e)", StringComparison.Ordinal) || p.Text.Contains("energy", StringComparison.Ordinal)))
            changes.Add($"your energy {energyWas} -> {energyNow}");

        if (Int(meWas?["hp"]) is { } hpWas && Int(meNow?["hp"]) is { } hpNow && hpNow > hpWas && own?.MayHeal != true)
            changes.Add($"your HP {hpWas} -> {hpNow}");

        // A change to the bot's powers that its own card made, which may move incoming_damage too.
        var ownPowerChange = own is not null && myPowersWas.Keys.Union(myPowersNow.Keys).Any(name =>
            myPowersWas.GetValueOrDefault(name).Amount != myPowersNow.GetValueOrDefault(name).Amount && own.ExplainsPower(name));
        foreach (var (name, power) in myPowersNow)
        {
            var before = myPowersWas.TryGetValue(name, out var p) ? p.Amount : 0;
            if (power.Amount <= before || power.Kind == "Debuff" || Reactive(power.Text))
                continue;
            if (own?.ExplainsPower(name) == true
                || myPowersWas.Values.Any(o => o.Text.Contains(name.ToLowerInvariant(), StringComparison.Ordinal)))
                continue;
            changes.Add($"your {name} {before} -> {power.Amount}");
        }

        // Enemies: one gone that the step can't have hit, and any change to a planned target it
        // didn't touch. A step that killed one may set off another leaving (minions flee).
        var enemiesWas = Enemies(was);
        var enemiesNow = Enemies(now);
        var gone = enemiesWas.Keys.Where(i => !enemiesNow.ContainsKey(i)).ToList();
        var killed = own is not null && gone.Any(own.Touches);
        foreach (var i in gone)
            if (own is null || !(own.Touches(i) || killed))
                changes.Add($"{EnemyName(enemiesWas[i], i)} is gone");
        foreach (var i in targets)
        {
            if (!enemiesWas.TryGetValue(i, out var e) || !enemiesNow.TryGetValue(i, out var n) || own?.Touches(i) == true)
                continue;
            var name = EnemyName(e, i);
            if (Int(e["hp"]) is { } a && Int(n["hp"]) is { } b && a != b)
                changes.Add($"{name} HP {a} -> {b}");
            if ((Int(e["block"]) ?? 0) is var ba && (Int(n["block"]) ?? 0) is var bb && ba != bb)
                changes.Add($"{name} Block {ba} -> {bb}");
            var pw = Powers(e);
            var pn = Powers(n);
            foreach (var power in pw.Keys.Union(pn.Keys))
            {
                var pa = pw.TryGetValue(power, out var x) ? x : default;
                var pb = pn.TryGetValue(power, out var y) ? y : default;
                if (pa.Amount != pb.Amount && !Reactive(pa.Text ?? "") && !Reactive(pb.Text ?? ""))
                    changes.Add($"{name} {power} {pa.Amount} -> {pb.Amount}");
            }
        }

        // incoming_damage moves with enemy debuffs, kills and the bot's own powers.
        if (Int(was.State["incoming_damage"]) is { } inWas && Int(now.State["incoming_damage"]) is { } inNow && inWas != inNow
            && !(own is not null && (own.TouchesEnemies || own.Type == "Power" || ownPowerChange)))
            changes.Add($"incoming damage {inWas} -> {inNow}");

        return changes.Count == 0 ? null : string.Join("; ", changes);
    }

    private readonly record struct PowerInfo(int Amount, string Kind, string Text);

    /// <summary>The powers of a creature entry by name: amount (1 for one shown without),
    /// kind, and its text in lower case.</summary>
    private static Dictionary<string, PowerInfo> Powers(JsonNode? creature)
    {
        var powers = new Dictionary<string, PowerInfo>(StringComparer.Ordinal);
        if (creature?["powers"] is not JsonArray list)
            return powers;
        foreach (var entry in list)
        {
            if (Str(entry?["name"]) is not { Length: > 0 } name || powers.ContainsKey(name))
                continue;
            powers[name] = new PowerInfo(Int(entry!["amount"]) ?? 1, Str(entry["kind"]) ?? "", (Str(entry["text"]) ?? "").ToLowerInvariant());
        }
        return powers;
    }

    /// <summary>A power that reacts to plays ("Whenever you play a Skill", "every 3 cards"),
    /// which the bot's own cards set off.</summary>
    private static bool Reactive(string lowerText) =>
        lowerText.Contains("when", StringComparison.Ordinal) || lowerText.Contains("every", StringComparison.Ordinal);

    /// <summary>The enemies still in the fight by their index ("i"), leaving out one at 0 HP
    /// that is coming back.</summary>
    private static Dictionary<int, JsonNode> Enemies(GameSnapshot s)
    {
        var enemies = new Dictionary<int, JsonNode>();
        if (s.State["enemies"] is not JsonArray list)
            return enemies;
        foreach (var e in list)
            if (e is not null && Int(e["i"]) is { } i && e["reviving"] is null
                && !(e["alive"] is JsonValue v && v.TryGetValue<bool>(out var alive) && !alive))
                enemies[i] = e;
        return enemies;
    }

    private static string EnemyName(JsonNode e, int i) => $"{Str(e["name"]) ?? "Enemy"} (e{i})";

    private static int? Int(JsonNode? node)
    {
        if (node is not JsonValue v)
            return null;
        if (v.TryGetValue<int>(out var i))
            return i;
        if (v.TryGetValue<long>(out var l))
            return (int)l;
        return v.TryGetValue<double>(out var d) ? (int)Math.Round(d) : null;
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
