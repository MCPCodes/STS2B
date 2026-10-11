using System.Globalization;
using System.Text.Json.Nodes;

namespace SpireMind.Core.Coop;

/// <summary>
/// A bot copy's side of the party's planning sessions (the conductor runs them from the human's
/// copy, see <see cref="PartyConductor"/>). It watches the snapshots the bot's tools see for a
/// session that is due (<see cref="PartyTriggers"/>) and reports that on /coop/info and /chat
/// (<see cref="Identity"/>); takes the conductor's ops on POST /coop/party (<see cref="Handle"/>);
/// holds the bot's pick screens while a session runs (<see cref="Holds"/>); hands the model its
/// turn brief and the plan once it is set; and takes its party_pitch (<see cref="Pitch"/>).
/// <para>Holding: before a session opens, only the act-start Ancient event, only after the
/// conductor said hello with auto on, for up to <see cref="PendingTimeout"/>; while one is open,
/// the pick screens (card_reward, shop, treasure, rewards, rest, event, card_select off combat)
/// until close or the session's time plus <see cref="Grace"/> on this copy's own clock. The map,
/// fights, hand_select and crystal_sphere are never held.</para>
/// Called from HTTP worker threads, the brain's task and the MCP endpoint; one lock guards it.
/// It never touches the game.
/// </summary>
public sealed class PartyBot
{
    private const int SettledReported = 9;
    private const int SettledKept = 30;

    /// <summary>How long a due session is reported after the bot last saw the screen behind it.</summary>
    private static readonly TimeSpan DueKept = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Action<string> _log;
    private readonly Func<DateTime> _clock;

    private bool _hello;
    private bool _auto;
    private readonly List<string> _settled = [];
    private int? _lastAct;
    private string? _screen;
    private string? _boss;
    private PartyDue? _due;
    private DateTime _dueSeen;
    private string? _pendingKey;
    private DateTime _pendingSince;
    private string? _holdingKey;
    private Session? _session;
    private Turn? _turn;
    private JsonObject? _pitch;
    private string? _plan;
    private string? _yours;
    private JsonArray? _calls;
    private JsonObject? _news;
    private bool _newsDelivered = true;

    public PartyBot(Action<string>? log = null, Func<DateTime>? clock = null)
    {
        _log = log ?? (_ => { });
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>How long the act-start Ancient pick is held before a session opens.</summary>
    public TimeSpan PendingTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long past a session's own time its holds last, in case close never comes.</summary>
    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(15);

    private sealed record Session(string Key, string Kind, int Act, int Seconds, DateTime Deadline);

    private sealed class Turn
    {
        public required string Session { get; init; }
        public required string Kind { get; init; }
        public int Round { get; init; }
        public int Rounds { get; init; }
        public int Seconds { get; init; }
        public string Ask { get; init; } = "";
        public JsonArray Said { get; init; } = [];
        public JsonArray Mark { get; init; } = [];
        public string? Plan { get; init; }
        public DateTime Received { get; init; }
        public bool Delivered { get; set; }
    }

    // ---- What the bot sees ----

    /// <summary>The screens a session holds: the pick screens off combat.</summary>
    public static bool IsPickScreen(GameSnapshot s) =>
        s.Screen is "card_reward" or "shop" or "treasure" or "rewards" or "rest" or "event"
        || (s.Screen == "card_select" && s.State["combat"] is null);

    /// <summary>Notes a snapshot the bot's tools saw: the screen, the act, the boss and whether a
    /// session is due. The menu and game over, and an act lower than the last one seen, forget the
    /// settled keys (a new run).</summary>
    public void Observe(GameSnapshot s)
    {
        string? log = null;
        lock (_gate)
            log = ObserveLocked(s, _clock());
        if (log is not null) Log(log);
    }

    private string? ObserveLocked(GameSnapshot s, DateTime now)
    {
        _screen = s.Screen;
        if (s.Screen is "menu" or "game_over")
        {
            _settled.Clear();
            _due = null;
            _pendingKey = null;
            _lastAct = null;
            return null;
        }
        var act = PartyTriggers.Act(s.State);
        if (act is { } a)
        {
            if (_lastAct is { } last && a < last)
            {
                _settled.Clear();
                _due = null;
                _pendingKey = null;
            }
            _lastAct = a;
        }
        if (s.Screen == "map" && s.State["boss"] is JsonValue b && b.TryGetValue<string>(out var boss) && boss.Length > 0)
            _boss = boss;

        if (_due is not null && now - _dueSeen > DueKept)
            _due = null;
        var due = PartyTriggers.Due(s.Screen, s.State);
        if (due is null)
            return null;
        if (due.Kind == PartyTriggers.Boss)
            SettleLocked(PartyTriggers.MidKey(due.Act));
        if (_settled.Contains(due.Key) || (due.Kind == PartyTriggers.MidAct && _settled.Contains(PartyTriggers.BossKey(due.Act))))
            return null;
        var isNew = _due?.Key != due.Key;
        _due = due;
        _dueSeen = now;
        return isNew ? $"party: due {due.Key} ({due.Why})" : null;
    }

    /// <summary>
    /// Whether the bot must not pick on this screen now (and notes the snapshot, as
    /// <see cref="Observe"/>). A session's hold that ran past its time, and the pending hold before
    /// a session opens once <see cref="PendingTimeout"/> passes, end here and settle their key.
    /// </summary>
    public bool Holds(GameSnapshot s)
    {
        var logs = new List<string>();
        bool holds;
        lock (_gate)
        {
            var now = _clock();
            if (ObserveLocked(s, now) is { } seen) logs.Add(seen);
            ExpireLocked(now, logs);
            holds = HoldKeyLocked(s, now, logs) is not null;
        }
        foreach (var line in logs) Log(line);
        return holds;
    }

    /// <summary>The Held text for act on this screen, or null when nothing holds it.</summary>
    public string? HeldText(GameSnapshot s)
    {
        var logs = new List<string>();
        string? text = null;
        lock (_gate)
        {
            var now = _clock();
            ExpireLocked(now, logs);
            if (HoldKeyLocked(s, now, logs) is { } key)
                text = PartyPrompts.Fill(PartyPrompts.Held, ("key", key), ("n", Math.Max(1, (int)Math.Ceiling(HoldLeftLocked(now).TotalSeconds))));
        }
        foreach (var line in logs) Log(line);
        return text;
    }

    // Caller holds _gate. The key that holds this screen now, or null.
    private string? HoldKeyLocked(GameSnapshot s, DateTime now, List<string> logs)
    {
        if (!IsPickScreen(s))
            return null;
        if (_session is { } session)
        {
            Holding(session.Key, s.Screen, logs);
            return session.Key;
        }
        if (!_hello || !_auto || s.Screen != "event" || _due is not { Kind: PartyTriggers.ActStart } due
            || PartyTriggers.Due(s.Screen, s.State)?.Key != due.Key || _settled.Contains(due.Key))
            return null;
        if (_pendingKey != due.Key)
        {
            _pendingKey = due.Key;
            _pendingSince = now;
        }
        if (now - _pendingSince < PendingTimeout)
        {
            Holding(due.Key, s.Screen, logs);
            return due.Key;
        }
        SettleLocked(due.Key);
        _pendingKey = null;
        _holdingKey = null;
        logs.Add($"party: released the hold for {due.Key} after {PendingTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s (no session opened)");
        return null;
    }

    private void Holding(string key, string screen, List<string> logs)
    {
        if (_holdingKey == key)
            return;
        _holdingKey = key;
        logs.Add($"party: holding {screen} for {key}");
    }

    private TimeSpan HoldLeftLocked(DateTime now)
    {
        if (_session is { } session)
            return session.Deadline - now;
        return PendingTimeout - (now - _pendingSince);
    }

    // Caller holds _gate. A session past its deadline ends here, as an early close.
    private void ExpireLocked(DateTime now, List<string> logs)
    {
        if (_session is not { } session || now < session.Deadline)
            return;
        _session = null;
        _turn = null;
        _holdingKey = null;
        SettleLocked(session.Key);
        SetNewsLocked(session.Key, PartyPrompts.Fill(PartyPrompts.ClosedEarly, ("reason", "it ran out of time")));
        logs.Add($"party: session {session.Key} ran past its time; released the hold");
    }

    // ---- The conductor's ops (POST /coop/party) ----

    /// <summary>
    /// One op from the conductor: hello {auto, plan, yours, calls, settled}, open {session, kind,
    /// act, seconds}, turn {session, round, rounds, seconds, ask, said, mark, plan} or close
    /// {session, reason, plan, yours, calls}. Returns {ok: true} or {ok: false, error}.
    /// </summary>
    public JsonObject Handle(JsonObject op)
    {
        var logs = new List<string>();
        JsonObject reply;
        lock (_gate)
        {
            var now = _clock();
            ExpireLocked(now, logs);
            reply = (PartyPitch.Str(op["op"]) ?? "") switch
            {
                "hello" => Hello(op),
                "open" => Open(op, now, logs),
                "turn" => TurnOp(op, now, logs),
                "close" => Close(op, logs),
                var other => Fail($"unknown op '{other}'"),
            };
        }
        foreach (var line in logs) Log(line);
        return reply;
    }

    private JsonObject Hello(JsonObject op)
    {
        _hello = true;
        _auto = Bool(op["auto"]);
        if (!_auto) _pendingKey = null;
        TakePlanLocked(op);
        if (op["settled"] is JsonArray settled)
            foreach (var key in settled.Select(PartyPitch.Str).OfType<string>())
                SettleLocked(key);
        return Ok();
    }

    private JsonObject Open(JsonObject op, DateTime now, List<string> logs)
    {
        if (PartyPitch.Str(op["session"]) is not { Length: > 0 } key)
            return Fail("session is required");
        if (_session?.Key == key)
            return Ok();
        if (_settled.Contains(key))
            return Fail($"{key} is settled here already");
        var seconds = Int(op["seconds"]) is { } s and > 0 ? s : 150;
        var kind = PartyPitch.Str(op["kind"]) ?? PartyTriggers.Manual;
        _session = new Session(key, kind, Int(op["act"]) ?? _lastAct ?? 0, seconds, now + TimeSpan.FromSeconds(seconds) + Grace);
        _turn = null;
        _pitch = null;
        _pendingKey = null;
        logs.Add($"party: session {key} opened ({seconds.ToString(CultureInfo.InvariantCulture)} s)");
        return Ok();
    }

    private JsonObject TurnOp(JsonObject op, DateTime now, List<string> logs)
    {
        var key = PartyPitch.Str(op["session"]);
        if (_session is not { } session || session.Key != key)
            return Fail($"no session {key ?? "(none)"} is open here");
        _turn = new Turn
        {
            Session = session.Key,
            Kind = session.Kind,
            Round = Int(op["round"]) ?? 1,
            Rounds = Int(op["rounds"]) ?? 1,
            Seconds = Int(op["seconds"]) is { } s and > 0 ? s : 40,
            Ask = PartyPitch.Str(op["ask"]) ?? "",
            Said = op["said"] is JsonArray said ? (JsonArray)said.DeepClone() : [],
            Mark = op["mark"] is JsonArray mark ? (JsonArray)mark.DeepClone() : [],
            Plan = PartyPitch.Str(op["plan"]),
            Received = now,
        };
        logs.Add($"party: turn in {session.Key}, round {_turn.Round.ToString(CultureInfo.InvariantCulture)}");
        return Ok();
    }

    private JsonObject Close(JsonObject op, List<string> logs)
    {
        var key = PartyPitch.Str(op["session"]);
        var reason = PartyPitch.Str(op["reason"]) ?? "done";
        TakePlanLocked(op);
        if (key is null)
            return Ok();
        SettleLocked(key);
        if (_session?.Key == key)
        {
            _session = null;
            _turn = null;
            _holdingKey = null;
            SetNewsLocked(key, reason == "done" ? PartyPrompts.Closed : PartyPrompts.Fill(PartyPrompts.ClosedEarly, ("reason", reason)));
            logs.Add($"party: closed {key} ({reason})");
        }
        return Ok();
    }

    private void TakePlanLocked(JsonObject op)
    {
        if (op.ContainsKey("plan")) _plan = PartyPitch.Str(op["plan"]) is { Length: > 0 } plan ? plan : null;
        if (op.ContainsKey("yours")) _yours = PartyPitch.Str(op["yours"]);
        if (op["calls"] is JsonArray calls) _calls = (JsonArray)calls.DeepClone();
    }

    private void SetNewsLocked(string key, string note)
    {
        var news = new JsonObject { ["closed"] = key, ["note"] = note };
        if (_plan is not null) news["plan"] = _plan;
        if (_yours is not null) news["yours"] = _yours;
        if (_calls is { Count: > 0 } calls) news["calls"] = calls.DeepClone();
        _news = news;
        _newsDelivered = false;
    }

    private void SettleLocked(string key)
    {
        if (_settled.Contains(key))
            return;
        _settled.Add(key);
        if (_settled.Count > SettledKept) _settled.RemoveRange(0, _settled.Count - SettledKept);
        if (_due?.Key == key) _due = null;
        if (_pendingKey == key) _pendingKey = null;
    }

    // ---- The model's side ----

    /// <summary>
    /// party_pitch: stores the bot's pitch for the open turn. Returns the pitch, or an error when no
    /// turn is open for this bot or say is missing. The caller speaks the say line.
    /// </summary>
    public (PartyPitch? Pitch, string? Error) Pitch(JsonObject args)
    {
        var logs = new List<string>();
        (PartyPitch?, string?) result;
        lock (_gate)
        {
            ExpireLocked(_clock(), logs);
            if (_turn is not { } turn)
            {
                result = (null, PartyPrompts.NotYourTurn);
            }
            else if (PartyPitch.Read(args, out var error) is not { } pitch)
            {
                result = (null, error);
            }
            else
            {
                var json = pitch.ToJson();
                json["session"] = turn.Session;
                json["round"] = turn.Round;
                _pitch = json;
                _turn = null;
                logs.Add($"party: pitched round {turn.Round.ToString(CultureInfo.InvariantCulture)}");
                result = (pitch, null);
            }
        }
        foreach (var line in logs) Log(line);
        return result;
    }

    /// <summary>True while a turn brief waits that no result has carried yet; flips once.</summary>
    public bool HasUndeliveredTurn
    {
        get { lock (_gate) return _turn is { Delivered: false }; }
    }

    /// <summary>True while a turn brief or the news of a closed session waits to reach the model.</summary>
    public bool HasNews
    {
        get
        {
            var logs = new List<string>();
            bool news;
            lock (_gate)
            {
                ExpireLocked(_clock(), logs);
                news = _turn is { Delivered: false } || (_news is not null && !_newsDelivered);
            }
            foreach (var line in logs) Log(line);
            return news;
        }
    }

    /// <summary>
    /// What a tool result carries under "party", or null: the turn brief while the bot's turn is
    /// open (until it pitches), and once, the news that a session it was in closed, with the plan.
    /// <paramref name="take"/> marks both as delivered (false for the curl routes).
    /// </summary>
    public JsonObject? Brief(bool take)
    {
        lock (_gate)
        {
            var now = _clock();
            JsonObject? party = null;
            if (_turn is { } turn)
            {
                party = new JsonObject
                {
                    ["session"] = turn.Session,
                    ["kind"] = turn.Kind,
                    ["round"] = turn.Round,
                    ["rounds"] = turn.Rounds,
                    ["your_turn"] = true,
                    ["seconds_left"] = Math.Max(0, turn.Seconds - (int)(now - turn.Received).TotalSeconds),
                    ["ask"] = turn.Ask,
                    ["said"] = turn.Said.DeepClone(),
                    ["mark"] = turn.Mark.DeepClone(),
                    ["plan"] = turn.Plan,
                };
                if (take) turn.Delivered = true;
            }
            if (_news is { } news && !_newsDelivered)
            {
                party ??= new JsonObject();
                foreach (var (k, v) in news)
                    party[k] = v?.DeepClone();
                if (take) _newsDelivered = true;
            }
            return party;
        }
    }

    /// <summary>The bot's own section and the party's calls, for a pick screen's snapshot; null
    /// on other screens or before any plan.</summary>
    public JsonObject? PickNotes(GameSnapshot s)
    {
        if (!IsPickScreen(s))
            return null;
        lock (_gate)
        {
            if (_yours is null && _calls is not { Count: > 0 })
                return null;
            var notes = new JsonObject { ["yours"] = _yours };
            notes["calls"] = _calls?.DeepClone() ?? new JsonArray();
            return notes;
        }
    }

    /// <summary>The whole party plan as the conductor last sent it, or null.</summary>
    public string? PlanText
    {
        get { lock (_gate) return _plan; }
    }

    /// <summary>What /coop/info and /chat report under "party" for the conductor.</summary>
    public JsonObject Identity()
    {
        var logs = new List<string>();
        JsonObject json;
        lock (_gate)
        {
            var now = _clock();
            ExpireLocked(now, logs);
            if (_due is not null && now - _dueSeen > DueKept)
                _due = null;
            json = new JsonObject
            {
                ["v"] = 1,
                ["auto"] = _auto,
                ["due"] = _due?.Key,
                ["settled"] = PartyPitch.Array(_settled.Skip(Math.Max(0, _settled.Count - SettledReported))),
                ["screen"] = _screen,
                ["act"] = _lastAct,
                ["boss"] = _boss,
                ["holding"] = _holdingKey is not null && (_session is not null || (_pendingKey is not null && now - _pendingSince < PendingTimeout)),
                ["session"] = _session?.Key,
                ["pitch"] = _pitch?.DeepClone(),
            };
        }
        foreach (var line in logs) Log(line);
        return json;
    }

    private void Log(string line)
    {
        try { _log(line); }
        catch (Exception) { /* a broken log can't be reported */ }
    }

    private static JsonObject Ok() => new() { ["ok"] = true };

    private static JsonObject Fail(string error) => new() { ["ok"] = false, ["error"] = error };

    private static bool Bool(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static int? Int(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<long>(out var l)) return (int)l;
        return v.TryGetValue<double>(out var d) ? (int)Math.Round(d) : null;
    }
}
