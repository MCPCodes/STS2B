using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace SpireMind.Core.Coop;

/// <summary>The planning sessions' timings; tests shorten them.</summary>
public sealed record PartyTimings
{
    /// <summary>How long after the first bot reports a due session the conductor waits for the
    /// others before it opens one.</summary>
    public TimeSpan GatherTime { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>The lead before round 1 of an act-start or manual session, for the human to say
    /// what he builds; his first line ends it.</summary>
    public TimeSpan LeadTime { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a bot has to pitch on its turn.</summary>
    public TimeSpan TurnTime { get; init; } = TimeSpan.FromSeconds(40);

    /// <summary>How long the conductor waits for the human's answer after round 1 when a bot asked
    /// him something; his first line ends it.</summary>
    public TimeSpan AnswerWait { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The longest act-start or manual session.</summary>
    public TimeSpan LongCap { get; init; } = TimeSpan.FromSeconds(150);

    /// <summary>The longest mid-act or boss session.</summary>
    public TimeSpan ShortCap { get; init; } = TimeSpan.FromSeconds(75);

    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>A saved plan older than this isn't taken up again when the human's copy starts.</summary>
    public TimeSpan PlanMaxAge { get; init; } = TimeSpan.FromHours(3);

    /// <summary>How often hello is sent again to a bot that reports another auto setting (a lost hello).</summary>
    public TimeSpan HelloRetry { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Runs the party's planning sessions from the human's copy (owned by <see cref="CoopRelay"/>,
/// which reaches every bot). Bots report a due session in their identity (<see cref="PartyBot"/>);
/// the first time a key it hasn't settled shows up, the conductor waits
/// <see cref="PartyTimings.GatherTime"/> for the others, then opens the session with every live
/// bot that is in the run, playing (api or subscription), not paused and able to plan.
/// <list type="bullet">
/// <item>act_start and manual: a lead for the human (ended by his first line), round 1 for every bot
/// at once, a wait for his answer when a bot asked him something, then round 2 one bot at a time in
/// port order, each seeing the pitches so far, for the bots that pitched in round 1. Round 2 is
/// skipped when fewer than two bots pitched and none asked anything.</item>
/// <item>mid and boss: round 1 only.</item>
/// </list>
/// A bot that doesn't pitch within <see cref="PartyTimings.TurnTime"/> is passed over and not asked
/// again in that session, and the cap
/// closes a session with whatever came in. On close the pitches are merged into the
/// <see cref="PartyPlan"/>, the plan is written (a temp file renamed over it) and pushed to every bot.
/// <para>Thread-safe: one lock guards the state, and nothing outside (the chat, the relay's sends,
/// files) is called while it is held. Ops go out through the send callback, which only queues them.</para>
/// </summary>
public sealed class PartyConductor
{
    private const int MaxHumanLines = 6;
    private const int MaxHumanLine = 200;
    private const int MaxAnnouncement = 280;
    private const int SettledSent = 9;
    private const string PlanFileName = "plan.md";
    private const string PlanJsonName = "plan.json";

    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly Action<int, JsonObject> _send;
    private readonly Action<string> _announce;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _clock;
    private readonly PartyTimings _t;
    private readonly string? _planFile;
    private readonly string? _humanName;
    private readonly Func<string?>? _humanCharacter;

    private readonly Dictionary<int, Member> _members = [];
    private readonly List<string> _settled = [];
    private PartyPlan _plan = new();
    private bool _auto;
    private Run? _run;
    private (string Key, string Kind, int Act, DateTime Since)? _gathering;
    private int _manualCount;
    private long _writes;

    /// <param name="send">Queues an op for the bot on that port (POST /coop/party). Must return at once.</param>
    /// <param name="announce">A system line in the human's chat.</param>
    /// <param name="log">The relay's log.</param>
    /// <param name="clock">Time source; defaults to <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="timings">Null for the defaults.</param>
    /// <param name="auto">Whether sessions open by themselves at first; /plan on|off sets it.</param>
    /// <param name="planFile">Where the plan is written (runs/party/plan.md under the notes
    /// root); null keeps it in memory only.</param>
    /// <param name="humanName">The human's name ("Mark"), for his chat lines and the plan.</param>
    /// <param name="humanCharacter">The human's character, for the plan's heading. Called with no
    /// lock held (it reads the chat hub).</param>
    public PartyConductor(Action<int, JsonObject> send, Action<string> announce, Action<string>? log = null,
        Func<DateTime>? clock = null, PartyTimings? timings = null, bool auto = true, string? planFile = null,
        string? humanName = null, Func<string?>? humanCharacter = null)
    {
        _send = send;
        _announce = announce;
        _log = log ?? (_ => { });
        _clock = clock ?? (() => DateTime.UtcNow);
        _t = timings ?? new PartyTimings();
        _auto = auto;
        _planFile = string.IsNullOrWhiteSpace(planFile) ? null : planFile;
        _humanName = string.IsNullOrWhiteSpace(humanName) ? null : humanName.Trim();
        _humanCharacter = humanCharacter;
    }

    private sealed class Member
    {
        public required int Port { get; init; }
        public string Label { get; set; } = "";
        public string Session { get; set; } = "";
        public string? Playing { get; set; }
        public bool Paused { get; set; }
        public bool? InRun { get; set; }
        public JsonObject? Party { get; set; }
        public bool Live { get; set; } = true;
        public DateTime HelloAt { get; set; } = DateTime.MinValue;

        public bool CanPlan => Live && Party is not null && (Playing is "api" or "subscription") && !Paused
                               && (InRun ?? (Int(Party["act"]) is not null));
        public string? Screen => PartyPitch.Str(Party?["screen"]);
        public int? Act => Int(Party?["act"]);
    }

    private sealed class Run
    {
        public required string Key { get; init; }
        public required string Kind { get; init; }
        public int Act { get; init; }
        public string? Boss { get; init; }
        public DateTime Started { get; init; }
        public TimeSpan Cap { get; init; }
        public List<int> Ports { get; } = [];
        public Dictionary<int, string> Labels { get; } = [];
        public Phase Phase { get; set; }
        public DateTime PhaseStarted { get; set; }
        public Dictionary<int, PartyPitch> First { get; } = [];
        public Dictionary<int, PartyPitch> Finals { get; } = [];
        public Dictionary<int, DateTime> AskedAt { get; } = [];
        public List<int> Order { get; } = [];      // ports in the order their finals came
        public HashSet<int> Passed { get; } = [];  // ports not asked in round 2 (silent in round 1, or timed out)
        public int? Current { get; set; }
        public DateTime TurnSent { get; set; }
        public List<(DateTime At, string Text)> Human { get; } = [];
        public DateTime? LastHumanAt { get; set; }
        public bool Long => Kind is PartyTriggers.ActStart or PartyTriggers.Manual;
    }

    private enum Phase { Lead, Round1, Answer, Round2 }

    /// <summary>Whether sessions open by themselves.</summary>
    public bool Auto
    {
        get { lock (_gate) return _auto; }
    }

    /// <summary>The running session's key, or null.</summary>
    public string? CurrentSession
    {
        get { lock (_gate) return _run?.Key; }
    }

    /// <summary>The keys settled here, oldest first.</summary>
    public IReadOnlyList<string> Settled
    {
        get { lock (_gate) return _settled.ToArray(); }
    }

    /// <summary>The plan as markdown, or null before any session.</summary>
    public string? PlanText
    {
        get
        {
            var character = HumanCharacter();
            lock (_gate) return _plan.IsEmpty ? null : _plan.Render(_humanName, character);
        }
    }

    // ---- Start ----

    /// <summary>Takes up the plan saved by an earlier start of the human's copy, when it is under
    /// <see cref="PartyTimings.PlanMaxAge"/> old. Never throws.</summary>
    public void Load()
    {
        if (_planFile is null)
            return;
        try
        {
            var json = Path.Combine(Path.GetDirectoryName(_planFile) ?? ".", PlanJsonName);
            if (!File.Exists(json))
                return;
            var age = _clock() - File.GetLastWriteTimeUtc(json);
            if (age > _t.PlanMaxAge)
            {
                Log($"party: the saved plan is {age.TotalHours.ToString("0.0", CultureInfo.InvariantCulture)} hours old; not taken up");
                return;
            }
            if (PartyPlan.FromJson(JsonNode.Parse(File.ReadAllText(json)) as JsonObject) is { } plan)
            {
                lock (_gate) _plan = plan;
                Log($"party: took up the saved plan (act {plan.Act.ToString(CultureInfo.InvariantCulture)}, last session {plan.LastSession ?? "none"})");
            }
        }
        catch (Exception ex)
        {
            Log($"party: couldn't read the saved plan: {ex.Message}");
        }
    }

    /// <summary>Ticks every <see cref="PartyTimings.TickInterval"/> until cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception ex) { Log($"party: tick failed: {ex}"); }
            try { await Task.Delay(_t.TickInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ---- What the relay tells it ----

    /// <summary>
    /// A bot's identity at a poll: its label in the human's chat, its copy's session, its brain,
    /// pause, whether it is in the run, and its "party" report (null from a copy that can't plan).
    /// A bot seen for the first time, or after its copy restarted, gets hello.
    /// </summary>
    public void Observe(int port, string label, string session, string? playing, bool paused, bool? inRun, JsonObject? party)
    {
        var character = HumanCharacter();
        var effects = new List<Action>();
        lock (_gate)
        {
            var now = _clock();
            _members.TryGetValue(port, out var member);
            var hello = member is null || member.Session != session || !member.Live;
            var wasInRun = member?.InRun;
            member ??= new Member { Port = port };
            _members[port] = member;
            member.Label = label;
            member.Session = session;
            member.Playing = playing;
            member.Paused = paused;
            member.InRun = inRun;
            member.Party = party is null ? null : (JsonObject)party.DeepClone();
            member.Live = true;
            if (party is null)
                return;

            // A new run: a bot that left the run and came back, or every bot that says its act below the
            // plan's (one alone may still show an old screen of the last act).
            var newRun = (wasInRun == false && inRun == true)
                         || (member.Act is { } act && act < _plan.Act
                             && _members.Values.Where(m => m.Live && m.Act is not null).All(m => m.Act < _plan.Act));
            if (newRun && ArchiveLocked(now, character, effects))
                hello = false; // the archive said hello to every bot

            foreach (var key in (party["settled"] as JsonArray ?? []).Select(PartyPitch.Str).OfType<string>())
                SettleLocked(key);

            if (!hello && Bool(party["auto"]) != _auto && now - member.HelloAt >= _t.HelloRetry)
                hello = true;
            if (hello)
                HelloLocked(member, now, character, effects);

            if (_run is { } run && run.Ports.Contains(port) && party["pitch"] is JsonObject pitch
                && PartyPitch.Str(pitch["session"]) == run.Key && PartyPitch.Read(pitch, out _) is { } p)
            {
                var round = Int(pitch["round"]) ?? 1;
                if (round == 1 && !run.First.ContainsKey(port))
                {
                    run.First[port] = p;
                    if (p.Question is not null) run.AskedAt[port] = now;
                    Log($"party: {label} pitched round 1 in {run.Key}");
                }
                else if (round == 2 && !run.Finals.ContainsKey(port))
                {
                    run.Finals[port] = p;
                    run.Order.Add(port);
                    Log($"party: {label} gave its final in {run.Key}");
                }
            }
        }
        Apply(effects);
    }

    /// <summary>The bot on this port stopped answering.</summary>
    public void Gone(int port)
    {
        lock (_gate)
            if (_members.TryGetValue(port, out var member))
                member.Live = false;
    }

    /// <summary>A line the human typed (not a command). During a session it goes into the turn
    /// briefs and the plan, ends the lead and answers a bot's question.</summary>
    public void OnHumanLine(string from, string text)
    {
        lock (_gate)
        {
            if (_run is not { } run)
                return;
            var now = _clock();
            run.LastHumanAt = now;
            if (run.Human.Count < MaxHumanLines && PartyPitch.Clean(text, MaxHumanLine) is { } line)
                run.Human.Add((now, line));
        }
    }

    // ---- The clock ----

    /// <summary>Opens a due session after the gather, and moves a running one on.</summary>
    public void Tick()
    {
        var character = HumanCharacter();
        var effects = new List<Action>();
        lock (_gate)
        {
            var now = _clock();
            if (_run is { } run)
            {
                AdvanceLocked(run, now, character, effects);
            }
            else if (!_auto)
            {
                _gathering = null;
            }
            else if (BestDueLocked() is { } due)
            {
                if (_gathering?.Key != due.Key)
                {
                    _gathering = (due.Key, due.Kind, due.Act, now);
                    Log($"party: {due.Key} is due; waiting {_t.GatherTime.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s for the other bots");
                }
                else if (now - _gathering.Value.Since >= _t.GatherTime)
                {
                    _gathering = null;
                    if (OpenLocked(due.Key, due.Kind, due.Act, now, character, effects) is { } opening)
                        effects.Add(() => _announce(opening));
                }
            }
            else
            {
                _gathering = null;
            }
        }
        Apply(effects);
    }

    // Caller holds _gate. The most advanced due key a live bot reports that isn't settled.
    private PartyDue? BestDueLocked()
    {
        PartyDue? best = null;
        foreach (var m in _members.Values.Where(m => m.Live && m.Party is not null))
        {
            if (PartyPitch.Str(m.Party!["due"]) is not { } key || _settled.Contains(key) || Parse(key) is not { } due)
                continue;
            if (best is null || due.Act > best.Act || (due.Act == best.Act && PartyTriggers.Rank(due.Kind) > PartyTriggers.Rank(best.Kind)))
                best = due;
        }
        if (best is { Kind: PartyTriggers.MidAct } mid && _settled.Contains(PartyTriggers.BossKey(mid.Act)))
            return null;
        return best;
    }

    private static PartyDue? Parse(string key)
    {
        var dash = key.IndexOf('-');
        if (dash < 2 || key[0] != 'A' || !int.TryParse(key[1..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var act))
            return null;
        var kind = key[(dash + 1)..] switch
        {
            "start" => PartyTriggers.ActStart,
            "mid" => PartyTriggers.MidAct,
            "boss" => PartyTriggers.Boss,
            _ => null,
        };
        return kind is null ? null : new PartyDue(key, kind, act, "reported");
    }

    // Caller holds _gate. Opens a session with the bots that can plan and returns the human's
    // opening line, or null when no bot can (an automatic key is then settled).
    private string? OpenLocked(string key, string kind, int act, DateTime now, string? character, List<Action> effects)
    {
        var bots = _members.Values
            .Where(m => m.CanPlan && !(m.Party!["settled"] is JsonArray s && s.Any(k => PartyPitch.Str(k) == key)))
            .OrderBy(m => m.Port)
            .ToList();
        if (bots.Count == 0)
        {
            if (kind != PartyTriggers.Manual)
            {
                SettleLocked(key);
                Log($"party: no bot can plan {key}; settled it");
            }
            return null;
        }

        var boss = bots.Select(b => PartyPitch.Str(b.Party!["boss"])).FirstOrDefault(b => b is not null) ?? _plan.Boss;
        var run = new Run
        {
            Key = key,
            Kind = kind,
            Act = act,
            Boss = boss,
            Started = now,
            Cap = kind is PartyTriggers.ActStart or PartyTriggers.Manual ? _t.LongCap : _t.ShortCap,
            PhaseStarted = now,
        };
        foreach (var b in bots)
        {
            run.Ports.Add(b.Port);
            run.Labels[b.Port] = b.Label;
            var op = new JsonObject
            {
                ["op"] = "open", ["session"] = key, ["kind"] = kind, ["act"] = act, ["seconds"] = (int)run.Cap.TotalSeconds,
            };
            var port = b.Port;
            effects.Add(() => _send(port, op));
        }
        _run = run;
        if (kind == PartyTriggers.Boss)
            SettleLocked(PartyTriggers.MidKey(act));
        Log($"party: opening {key} with {string.Join(", ", bots.Select(b => b.Label))}");

        if (run.Long)
            run.Phase = Phase.Lead;
        else
            StartRound1Locked(run, now, character, effects);

        var cap = PartyPrompts.Span(run.Cap);
        var name = _humanName is null ? "" : _humanName + ", ";
        var opening = kind switch
        {
            PartyTriggers.ActStart => PartyPrompts.Fill(PartyPrompts.OpeningActStart, ("act", act), ("cap", cap), ("name", name)),
            PartyTriggers.Manual => PartyPrompts.Fill(PartyPrompts.OpeningManual, ("cap", cap), ("name", name)),
            PartyTriggers.MidAct => PartyPrompts.Fill(PartyPrompts.OpeningMid, ("cap", cap)),
            _ => PartyPrompts.Fill(PartyPrompts.OpeningBoss, ("boss", boss ?? "the boss"), ("cap", cap)),
        };
        // "tell them" opens the sentence when the human's name isn't known.
        return name.Length == 0 ? opening.Replace(". tell them", ". Tell them", StringComparison.Ordinal) : opening;
    }

    // Caller holds _gate.
    private void AdvanceLocked(Run run, DateTime now, string? character, List<Action> effects)
    {
        // A bot whose copy went away is passed over, and so is one that turned the session down
        // (it settled the key by itself, its Ancient hold having run out just before the open).
        run.Ports.RemoveAll(p => !(_members.TryGetValue(p, out var m) && m.Live && m.Party is not null) || Refused(m, run.Key));
        if (run.Ports.Count == 0)
        {
            FinishLocked(run, now, "no bot left", character, effects, announce: false);
            Log($"party: every bot in {run.Key} went away");
            return;
        }
        if (now - run.Started >= run.Cap)
        {
            Log($"party: {run.Key} reached its cap of {run.Cap.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s");
            FinishLocked(run, now, "done", character, effects, announce: true);
            return;
        }

        switch (run.Phase)
        {
            case Phase.Lead:
                if (run.Human.Count > 0 || run.LastHumanAt is not null || now - run.PhaseStarted >= _t.LeadTime)
                    StartRound1Locked(run, now, character, effects);
                break;

            case Phase.Round1:
                var missing = run.Ports.Where(p => !run.First.ContainsKey(p)).ToList();
                if (missing.Count > 0 && now - run.PhaseStarted < _t.TurnTime)
                    break;
                // A bot that didn't pitch in round 1 isn't asked in round 2: it is most likely stuck,
                // and each turn it lets run out holds the human for another TurnTime. A late round-1
                // pitch from it still goes into the plan.
                foreach (var p in missing)
                {
                    TimedOutLocked(run, p, effects);
                    run.Passed.Add(p);
                }
                var asked = run.AskedAt.Count > 0;
                var round2 = run.Long && (run.Ports.Count(p => !run.Passed.Contains(p)) >= 2 || asked);
                if (!round2)
                {
                    FinishLocked(run, now, "done", character, effects, announce: true);
                }
                else if (asked && !AnsweredLocked(run))
                {
                    run.Phase = Phase.Answer;
                    run.PhaseStarted = now;
                    Log($"party: waiting up to {_t.AnswerWait.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s for an answer in {run.Key}");
                }
                else
                {
                    StartRound2Locked(run, now, character, effects);
                }
                break;

            case Phase.Answer:
                if (AnsweredLocked(run) || now - run.PhaseStarted >= _t.AnswerWait)
                    StartRound2Locked(run, now, character, effects);
                break;

            case Phase.Round2:
                if (run.Current is { } current && !run.Finals.ContainsKey(current) && run.Ports.Contains(current))
                {
                    if (now - run.TurnSent < _t.TurnTime)
                        break;
                    TimedOutLocked(run, current, effects);
                }
                NextTurnLocked(run, now, character, effects);
                break;
        }
    }

    private bool Refused(Member m, string key)
    {
        if (m.Party is not { } party || PartyPitch.Str(party["session"]) == key
            || !(party["settled"] is JsonArray settled && settled.Any(k => PartyPitch.Str(k) == key)))
            return false;
        Log($"party: {m.Label} isn't in {key} (it settled it already)");
        return true;
    }

    private bool AnsweredLocked(Run run) =>
        run.LastHumanAt is { } said && run.AskedAt.Count > 0 && said >= run.AskedAt.Values.Min();

    private void TimedOutLocked(Run run, int port, List<Action> effects)
    {
        var label = run.Labels.GetValueOrDefault(port, $"The bot on {port.ToString(CultureInfo.InvariantCulture)}");
        Log($"party: {label} didn't pitch in time in {run.Key}");
        var line = PartyPrompts.Fill(PartyPrompts.TurnTimedOut, ("bot", label));
        effects.Add(() => _announce(line));
        // On a round-2 turn it isn't asked again, and its round-1 pitch, if any, stands as its final.
        if (run.Phase != Phase.Round2)
            return;
        run.Passed.Add(port);
        if (!run.Finals.ContainsKey(port) && run.First.TryGetValue(port, out var first))
        {
            run.Finals[port] = first;
            run.Order.Add(port);
        }
    }

    private void StartRound1Locked(Run run, DateTime now, string? character, List<Action> effects)
    {
        run.Phase = Phase.Round1;
        run.PhaseStarted = now;
        var rounds = run.Long && run.Ports.Count >= 2 ? 2 : 1;
        var plan = _plan.IsEmpty ? null : _plan.Render(_humanName, character);
        foreach (var port in run.Ports)
            SendTurnLocked(run, port, 1, rounds, now, plan, effects);
    }

    private void StartRound2Locked(Run run, DateTime now, string? character, List<Action> effects)
    {
        run.Phase = Phase.Round2;
        run.PhaseStarted = now;
        run.Current = null;
        NextTurnLocked(run, now, character, effects);
    }

    private void NextTurnLocked(Run run, DateTime now, string? character, List<Action> effects)
    {
        var next = run.Ports.FirstOrDefault(p => !run.Finals.ContainsKey(p) && !run.Passed.Contains(p), -1);
        if (next < 0)
        {
            FinishLocked(run, now, "done", character, effects, announce: true);
            return;
        }
        run.Current = next;
        run.TurnSent = now;
        SendTurnLocked(run, next, 2, 2, now, _plan.IsEmpty ? null : _plan.Render(_humanName, character), effects);
    }

    private void SendTurnLocked(Run run, int port, int round, int rounds, DateTime now, string? plan, List<Action> effects)
    {
        var seconds = (int)_t.TurnTime.TotalSeconds;
        var said = new JsonArray();
        if (round == 2)
        {
            foreach (var p in run.Ports.Concat(run.First.Keys).Distinct().Where(run.First.ContainsKey))
                said.Add(new JsonObject { ["from"] = Label(run, p), ["round"] = 1, ["pitch"] = run.First[p].ToJson() });
            foreach (var p in run.Order)
                said.Add(new JsonObject { ["from"] = Label(run, p), ["round"] = 2, ["pitch"] = run.Finals[p].ToJson() });
        }
        var mark = new JsonArray(run.Human.Select(h => (JsonNode)new JsonObject
        {
            ["text"] = h.Text,
            ["seconds_ago"] = Math.Max(0, (int)(now - h.At).TotalSeconds),
        }).ToArray());
        var op = new JsonObject
        {
            ["op"] = "turn",
            ["session"] = run.Key,
            ["round"] = round,
            ["rounds"] = rounds,
            ["seconds"] = seconds,
            ["ask"] = Ask(run, round, rounds, seconds),
            ["said"] = said,
            ["mark"] = mark,
            ["plan"] = plan,
        };
        effects.Add(() => _send(port, op));
    }

    private static string Label(Run run, int port) => run.Labels.GetValueOrDefault(port, $"The bot on {port.ToString(CultureInfo.InvariantCulture)}");

    private string Ask(Run run, int round, int rounds, int seconds)
    {
        var boss = run.Boss ?? "the boss";
        if (run.Kind == PartyTriggers.MidAct)
            return PartyPrompts.Fill(PartyPrompts.Mid, ("act", run.Act), ("seconds", seconds), ("boss", boss));
        if (run.Kind == PartyTriggers.Boss)
            return PartyPrompts.Fill(PartyPrompts.Boss, ("boss", boss), ("seconds", seconds));
        if (round == 1)
            return PartyPrompts.Fill(run.Kind == PartyTriggers.Manual ? PartyPrompts.ManualRound1 : PartyPrompts.ActStartRound1,
                ("act", run.Act), ("rounds", rounds), ("seconds", seconds));
        var answer = run.AskedAt.Count == 0 ? "" : AnsweredLocked(run) ? PartyPrompts.Answered : PartyPrompts.NotAnswered;
        return PartyPrompts.Fill(PartyPrompts.ActStartRound2, ("act", run.Act), ("seconds", seconds), ("answer", answer))
            .Replace("  ", " ", StringComparison.Ordinal);
    }

    // Caller holds _gate. Merges the pitches, writes the plan, settles the key, sends close to every
    // bot and (announce) tells the human what was set.
    private void FinishLocked(Run run, DateTime now, string reason, string? character, List<Action> effects, bool announce)
    {
        _run = null;
        var finals = new List<(string Label, PartyPitch Pitch)>();
        foreach (var port in run.Order.Concat(run.Ports).Concat(run.First.Keys).Distinct())
        {
            var pitch = run.Finals.TryGetValue(port, out var f) ? f : run.First.TryGetValue(port, out var r1) ? r1 : null;
            if (pitch is not null) finals.Add((Label(run, port), pitch));
        }
        var questions = new List<PartyQuestion>();
        foreach (var (port, at) in run.AskedAt.OrderBy(a => a.Value))
        {
            var answer = run.Human.FirstOrDefault(h => h.At >= at).Text;
            var guess = answer is null && run.Finals.TryGetValue(port, out var final) && final != run.First[port] ? final.Say : null;
            questions.Add(new PartyQuestion(Label(run, port), run.First[port].Question!, answer, guess));
        }
        var status = reason == "done" ? "done" : reason == PartyPrompts.SkippedReason ? "skipped" : reason;
        _plan.Merge(new PartySessionResult(run.Key, run.Kind, run.Act, status, now, run.Boss, finals,
            run.Human.Select(h => h.Text).ToList(), questions));
        SettleLocked(run.Key);
        if (run.Kind == PartyTriggers.Boss)
            SettleLocked(PartyTriggers.MidKey(run.Act));

        var markdown = _plan.Render(_humanName, character);
        var json = _plan.ToJson().ToJsonString();
        var written = _planFile is not null;
        if (written)
        {
            var seq = ++_writes;
            effects.Add(() => _ = Task.Run(() => WritePlan(seq, markdown, json)));
        }

        foreach (var m in _members.Values.Where(m => m.Live && m.Party is not null))
        {
            var op = CloseOp(run.Key, reason, markdown, m.Label);
            var port = m.Port;
            effects.Add(() => _send(port, op));
        }

        var seconds = (now - run.Started).TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        Log($"party: closed {run.Key} ({reason}) in {seconds} s" + (written ? $"; plan written to {_planFile}" : ""));
        if (announce)
        {
            var line = Summary(run, finals, written);
            effects.Add(() => _announce(line));
        }
    }

    private JsonObject CloseOp(string key, string reason, string plan, string label) => new()
    {
        ["op"] = "close",
        ["session"] = key,
        ["reason"] = reason,
        ["plan"] = plan,
        ["yours"] = _plan.Yours(label),
        ["calls"] = PartyPitch.Array(_plan.CallList()),
    };

    private string Summary(Run run, List<(string Label, PartyPitch Pitch)> finals, bool written)
    {
        var saved = written ? $" Saved in {ShownPath()}." : "";
        if (finals.Count == 0)
            return Cut("Planning ended with no pitches; the plan stays as it was.");
        var calls = finals.SelectMany(f => f.Pitch.Calls).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();
        if (run.Kind == PartyTriggers.Boss)
            return Cut((calls.Count > 0 ? $"Boss plan set: {string.Join("; ", calls)}." : "Boss plan: no calls came in.") + saved);
        var parts = finals.Select(f =>
        {
            var lane = f.Pitch.Archetype ?? f.Pitch.Role ?? "flex";
            return f.Pitch.Role is { } role && f.Pitch.Archetype is not null ? $"{f.Label} {lane} ({role})" : $"{f.Label} {lane}";
        }).Concat(calls);
        return Cut($"Party plan set: {string.Join("; ", parts)}.{saved}");
    }

    private string ShownPath()
    {
        var path = _planFile!.Replace('\\', '/');
        const string tail = "runs/party/" + PlanFileName;
        return path.EndsWith(tail, StringComparison.Ordinal) ? tail : path;
    }

    private static string Cut(string text) => text.Length <= MaxAnnouncement ? text : text[..(MaxAnnouncement - 3)].TrimEnd() + "...";

    // Caller holds _gate.
    private void HelloLocked(Member m, DateTime now, string? character, List<Action> effects)
    {
        m.HelloAt = now;
        var op = new JsonObject
        {
            ["op"] = "hello",
            ["auto"] = _auto,
            ["plan"] = _plan.IsEmpty ? null : _plan.Render(_humanName, character),
            ["yours"] = _plan.Yours(m.Label),
            ["calls"] = PartyPitch.Array(_plan.CallList()),
            ["settled"] = PartyPitch.Array(_settled.Skip(Math.Max(0, _settled.Count - SettledSent))),
        };
        var port = m.Port;
        effects.Add(() => _send(port, op));
    }

    // Caller holds _gate. Moves the old plan aside for a new run and starts a blank one.
    private bool ArchiveLocked(DateTime now, string? character, List<Action> effects)
    {
        if (_plan.IsEmpty && _settled.Count == 0)
            return false;
        Log("party: a new run started; the old plan is put aside");
        _plan = new PartyPlan();
        _settled.Clear();
        _gathering = null;
        if (_planFile is not null)
        {
            var seq = ++_writes;
            var stamp = (now.Kind == DateTimeKind.Local ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc).ToLocalTime())
                .ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);
            effects.Add(() => _ = Task.Run(() => ArchivePlan(seq, stamp)));
        }
        foreach (var m in _members.Values.Where(m => m.Live && m.Party is not null))
            HelloLocked(m, now, character, effects);
        return true;
    }

    private void SettleLocked(string key)
    {
        if (_settled.Contains(key))
            return;
        _settled.Add(key);
        if (_settled.Count > 60) _settled.RemoveRange(0, _settled.Count - 60);
        if (_gathering?.Key == key) _gathering = null;
    }

    // ---- /plan ----

    /// <summary>
    /// /plan [skip|show|on|off]: starts a session now, ends the running one, shows the plan, or
    /// turns the automatic sessions on or off. Returns the line for the chat at once.
    /// </summary>
    public string Command(string from, string args)
    {
        var character = HumanCharacter();
        var effects = new List<Action>();
        string reply;
        lock (_gate)
        {
            var now = _clock();
            switch (args.Trim().ToLowerInvariant())
            {
                case "":
                    if (_run is not null)
                    {
                        reply = PartyPrompts.AlreadyRunning;
                        break;
                    }
                    if (_members.Values.Any(m => m.Live && m.Screen is "combat" or "hand_select"))
                    {
                        reply = PartyPrompts.InFight;
                        break;
                    }
                    var act = _members.Values.Where(m => m.Live).Select(m => m.Act ?? 0).DefaultIfEmpty(0).Max();
                    if (act == 0) act = Math.Max(1, _plan.Act);
                    var n = _manualCount;
                    string key;
                    do key = $"A{act.ToString(CultureInfo.InvariantCulture)}-plan{(++n).ToString(CultureInfo.InvariantCulture)}";
                    while (_settled.Contains(key));
                    var opened = OpenLocked(key, PartyTriggers.Manual, act, now, character, effects);
                    if (opened is not null) _manualCount = n;
                    reply = opened ?? PartyPrompts.NoBots;
                    break;
                case "skip" or "stop" or "end":
                    if (_run is not { } run)
                    {
                        reply = PartyPrompts.NothingToSkip;
                        break;
                    }
                    FinishLocked(run, now, PartyPrompts.SkippedReason, character, effects, announce: false);
                    reply = PartyPrompts.Skipped;
                    break;
                case "show":
                    reply = ShowLocked();
                    break;
                case "on" or "off":
                    _auto = args.Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
                    _gathering = null;
                    foreach (var m in _members.Values.Where(m => m.Live && m.Party is not null))
                        HelloLocked(m, now, character, effects);
                    reply = _auto ? PartyPrompts.AutoOn : PartyPrompts.AutoOff;
                    break;
                default:
                    reply = PartyPrompts.Usage;
                    break;
            }
        }
        Apply(effects);
        return reply;
    }

    private string ShowLocked()
    {
        if (_plan.IsEmpty)
            return PartyPrompts.NoPlan;
        var lines = new List<string>();
        foreach (var s in _plan.Bots)
        {
            var parts = new[] { s.Role, s.Archetype }.OfType<string>().ToList();
            if (s.Takes.Count > 0) parts.Add("takes " + string.Join(", ", s.Takes));
            lines.Add($"{s.Label}: {(parts.Count > 0 ? string.Join("; ", parts) : "no lane yet")}.");
        }
        var calls = _plan.CallList();
        lines.Add(calls.Count > 0 ? $"Calls: {string.Join("; ", calls)}." : "No party calls yet.");
        return string.Join("\n", lines);
    }

    // ---- Files, done with no lock held ----

    private void WritePlan(long seq, string markdown, string json)
    {
        lock (_fileGate)
        {
            if (seq < Interlocked.Read(ref _writes))
                return; // a newer write or archive follows
            try
            {
                var dir = Path.GetDirectoryName(_planFile!)!;
                Directory.CreateDirectory(dir);
                Replace(_planFile!, markdown);
                Replace(Path.Combine(dir, PlanJsonName), json);
            }
            catch (Exception ex)
            {
                Log($"party: couldn't write the plan to {_planFile}: {ex.Message}");
            }
        }
    }

    private static void Replace(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private void ArchivePlan(long seq, string stamp)
    {
        lock (_fileGate)
        {
            if (seq < Interlocked.Read(ref _writes))
                return; // a newer plan was written over the old one already
            try
            {
                var dir = Path.GetDirectoryName(_planFile!)!;
                if (File.Exists(_planFile))
                {
                    var target = Path.Combine(dir, stamp + ".md");
                    for (var n = 2; File.Exists(target); n++)
                        target = Path.Combine(dir, $"{stamp}-{n.ToString(CultureInfo.InvariantCulture)}.md");
                    File.Move(_planFile!, target);
                    Log($"party: the old plan is now {target}");
                }
                var json = Path.Combine(dir, PlanJsonName);
                if (File.Exists(json)) File.Delete(json);
            }
            catch (Exception ex)
            {
                Log($"party: couldn't put the old plan aside: {ex.Message}");
            }
        }
    }

    // ---- Helpers ----

    private void Apply(List<Action> effects)
    {
        foreach (var effect in effects)
        {
            try { effect(); }
            catch (Exception ex) { Log($"party: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private string? HumanCharacter()
    {
        try { return _humanCharacter?.Invoke(); }
        catch (Exception) { return null; }
    }

    private void Log(string line)
    {
        try { _log(line); }
        catch (Exception) { /* a broken log can't be reported */ }
    }

    private static bool Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static int? Int(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)l;
        return v.TryGetValue<double>(out var d) ? (int)Math.Round(d) : null;
    }
}
