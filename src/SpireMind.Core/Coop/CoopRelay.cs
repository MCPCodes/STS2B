using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
// The mod compiles Core with global usings for Godot and the game, and Godot has its own
// HttpClient. The aliases keep the .NET types here without importing whole namespaces.
using HttpClient = System.Net.Http.HttpClient;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SpireMind.Core.Coop;

/// <summary>
/// The human's side of co-op chat: lets Mark talk to the bot copies from his own window. It
/// finds bot copies on localhost (GET /coop/info), sends them what he types and the commands
/// that belong to them (/start, /pause, /model, /fast and the rest, answered with each bot's name in
/// front), reads their chat logs (GET /chat?since=) into his log and onto their players as
/// speech bubbles (<see cref="ICoopBots.ShowLine"/>) with a thought cloud while a bot decides,
/// and passes each bot's lines to the other bots as party chat. It adds /bots, /bot add,
/// /bot stop, /party and /plan, and asks the bots to look for the lobby when the human's copy opens
/// one (<see cref="NudgeBots"/>). It owns the <see cref="PartyConductor"/>, which runs the party's
/// planning sessions over the same workers (POST /coop/party). Runs only in the human's copy.
///
/// Each bot goes by its nameplate on the human's screen ("Ironclad", "Ironclad 2", from
/// <see cref="ICoopBots.NameOf"/>), else its character, numbered from the second bot on one
/// character. Chat labels, /bots and addressing a bot by name ("Ironclad 2, block") all use it.
///
/// Every bot has one worker task that makes that bot's HTTP calls strictly in order (the lines
/// and commands to send, and a poll of its log when there is nothing to send), so a poll never
/// runs during a POST and a command's reply, recorded from the POST, is never shown again by
/// the poll. Only http://127.0.0.1 is ever contacted, by port number, and redirects are never
/// followed. ChatHub calls are made with no lock of the relay's held. See docs/chat.md.
///
/// Outside co-op (no co-op run or lobby, see the inCoop callback) Mark's window works as in
/// solo play even while bot copies are linked: only a command or line that starts with a
/// linked bot's character goes to that bot, everything else stays with this copy.
/// </summary>
public sealed partial class CoopRelay : IDisposable
{
    /// <summary>The commands typed in the human's window that go to the bot copies in co-op
    /// while any exist (and, in co-op or not, any of them or /help with a linked bot's name).</summary>
    public static IReadOnlyList<string> ForwardedCommands { get; } =
        ["start", "stop", "pause", "resume", "budget", "model", "effort", "fast"];

    /// <summary>What /bot add accepts, with or without "The".</summary>
    public static IReadOnlyList<string> CharacterChoices { get; } =
        ["Ironclad", "Silent", "Defect", "Regent", "Necrobinder", "Random"];

    /// <summary>The end of /help while bot copies exist.</summary>
    public const string CoopHelpNote =
        "In co-op, /start, /stop, /pause, /resume, /budget, /model, /effort and /fast go to every bot; put a bot's name first to send to one (/model Silent sonnet, /help Silent).";

    private const string NotAnsweringYet = "No bot copy is answering yet; try again in a moment.";
    private const string BotUsage = "Use /bot add [Ironclad|Silent|Defect|Regent|Necrobinder|Random] or /bot stop [name].";
    private const string QuitPath = "/coop/quit";
    private const string JoinNowPath = "/coop/join-now";
    private const string PartyPath = "/coop/party";
    private const string NoLauncher = "This build can't start bot copies; start one with scripts/coop-bot.sh.";
    private const int MaxFailureLength = 160;
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan PartyWindow = TimeSpan.FromMinutes(1);
    // The game side waits up to ten seconds for its copies to quit, then ends the stragglers.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    private readonly ChatHub _chat;
    private readonly CoopRelayOptions _options;
    private readonly ICoopBots? _coopBots;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _clock;
    private readonly Func<bool>? _inCoop;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _stopToken;
    private readonly SemaphoreSlim _wake = new(0);

    // Guards _peers, _ignored and every link's identity fields, queue hand-off and Live flag.
    // Never held while calling the chat hub, StartCopyAsync or ShowLine (reading Known, a
    // snapshot, is fine).
    private readonly object _gate = new();
    private readonly Dictionary<int, Link> _peers = [];   // the latest link per port, live or not
    private readonly HashSet<int> _ignored = [];         // ports already logged as not a bot
    private int _liveCount;
    private int _starting;
    private int _started;
    private int _disposed;
    private int _cloudFailed;
    private volatile bool _partyChat;
    private Func<string, string, string, string?>? _filter;
    private Action<ChatLine>? _onLine;
    private Func<string, string, string>? _botsCommand, _botCommand, _partyCommand, _planCommand;
    private readonly PartyConductor _conductor;

    /// <param name="chat">The human copy's chat hub.</param>
    /// <param name="options">Ports, timings and limits.</param>
    /// <param name="coopBots">Starting bot copies and drawing bubbles, when the game offers it.</param>
    /// <param name="log">Log lines (no secrets ever go in them).</param>
    /// <param name="clock">Time source for the party chat limit; defaults to <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="inCoop">True while the human's game is in a co-op run or lobby. Called from
    /// the chat's threads, so it must return at once and not touch the game. Outside co-op the
    /// commands and lines go to a bot only when they name it first. Null counts as always in
    /// co-op; a callback that throws counts as not in co-op.</param>
    public CoopRelay(ChatHub chat, CoopRelayOptions options, ICoopBots? coopBots = null,
        Action<string>? log = null, Func<DateTime>? clock = null, Func<bool>? inCoop = null)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(options);
        _chat = chat;
        _options = options;
        _coopBots = coopBots;
        _log = log ?? (_ => { });
        _clock = clock ?? (() => DateTime.UtcNow);
        _inCoop = inCoop;
        _partyChat = options.PartyChat;
        _stopToken = _stop.Token;
        _http = options.Handler is { } handler
            ? new HttpClient(handler, disposeHandler: false)
            : new HttpClient(NewLoopbackHandler(options), disposeHandler: true);
        // Each call has its own deadline.
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        _http.MaxResponseContentBufferSize = MaxResponseBytes;
        _conductor = new PartyConductor(SendPartyOp, Announce, line => _log(line), _clock, options.PartyTimings,
            options.Planning, options.PartyPlanFile, options.HumanName, options.HumanCharacter);
    }

    /// <summary>The party's planning sessions, run from here.</summary>
    public PartyConductor Conductor => _conductor;

    /// <summary>
    /// The handler the relay uses when the options give none: no proxy from the environment, and
    /// redirects never followed, so whatever answers on a probed port can't send the relay (and
    /// Mark's lines in a POST) to another host. A 3xx then counts as "not a bot".
    /// </summary>
    public static SocketsHttpHandler NewLoopbackHandler(CoopRelayOptions options) => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        ConnectTimeout = options.ProbeTimeout > TimeSpan.Zero ? options.ProbeTimeout : TimeSpan.FromSeconds(1),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
    };

    /// <summary>True while the human's game is in a co-op run or lobby (always, without an
    /// inCoop callback). Lock-free.</summary>
    public bool InCoop
    {
        get
        {
            if (_inCoop is not { } inCoop)
                return true;
            try { return inCoop(); }
            catch (Exception) { return false; }
        }
    }

    /// <summary>True while a bot copy is linked (it answered within the last few polls) or the
    /// game knows of bot copies it started. Lock-free and quick: the brain's gate calls it.</summary>
    public bool BotsExist =>
        Volatile.Read(ref _disposed) == 0 && (Volatile.Read(ref _liveCount) > 0 || KnownBots().Count > 0);

    /// <summary>The bot copies talked to in this relay's life, sorted by port; those that
    /// stopped answering have Live false.</summary>
    public IReadOnlyList<BotPeer> Peers
    {
        get
        {
            lock (_gate)
                return _peers.Values.OrderBy(l => l.Port)
                    .Select(l => new BotPeer(l.Port, l.Session, l.Character, l.NetId, l.Playing, l.Paused, l.Live,
                        LabelLocked(l), l.Status, l.Thinking))
                    .ToArray();
        }
    }

    /// <summary>Whether each bot's lines are passed to the other bots. /party sets it.</summary>
    public bool PartyChat
    {
        get => _partyChat;
        set => _partyChat = value;
    }

    /// <summary>
    /// Adds /bots, /bot and /party, takes over <see cref="ChatHub.CommandFilter"/>, listens for
    /// the human's lines and starts looking for bot copies. Returns at once. Call it once.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("The co-op relay is already started.");

        _botsCommand = ListBots;
        _botCommand = BotCommand;
        _partyCommand = PartyCommand;
        _chat.AddCommand("bots", "", "lists the bot copies", _botsCommand);
        _chat.AddCommand("bot", "add [character] | stop [name]", "starts a bot copy, or asks one (or all) to leave and quit", _botCommand);
        _chat.AddCommand("party", "[on|off]", "sets whether bots hear each other", _partyCommand);
        _planCommand = (from, args) => _conductor.Command(from, args);
        _chat.AddCommand("plan", "[skip|show|on|off]", "starts or ends the bots' planning session, shows the plan, or turns the automatic ones on or off", _planCommand);
        _filter = Filter;
        _chat.CommandFilter = _filter;
        _onLine = OnLine;
        _chat.LineAdded += _onLine;
        _ = Task.Run(() => LoopAsync(_stopToken));
        _conductor.Load();
        _ = Task.Run(() => _conductor.RunAsync(_stopToken));
        _log($"looking for bot copies on ports {string.Join(", ", CandidatePorts())}");
    }

    /// <summary>Stops every loop and call, gives the command filter back (if still the
    /// relay's), clears the /help note and removes /bots, /bot and /party.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        if (_onLine is not null) _chat.LineAdded -= _onLine;
        if (_filter is not null && _chat.CommandFilter == _filter) _chat.CommandFilter = null;
        if (_started != 0) _chat.HelpNote = null;
        if (_botsCommand is not null) _chat.RemoveCommand("bots", _botsCommand);
        if (_botCommand is not null) _chat.RemoveCommand("bot", _botCommand);
        if (_partyCommand is not null) _chat.RemoveCommand("party", _partyCommand);
        if (_planCommand is not null) _chat.RemoveCommand("plan", _planCommand);
        lock (_gate)
        {
            foreach (var link in _peers.Values) link.Live = false;
            Volatile.Write(ref _liveCount, 0);
        }
        _http.Dispose();
    }

    // ---- Discovery ----

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProbeAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log($"probing failed: {ex.Message}");
            }
            RefreshHelpNote();

            try
            {
                await _wake.WaitAsync(_options.ProbeInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // ProbePorts and the ports of the bot copies the game started, without this copy's own.
    private IReadOnlyList<int> CandidatePorts() =>
        _options.ProbePorts.Concat(KnownBots().Select(k => k.Port))
            .Where(p => p is >= 1 and <= 65535 && p != _options.OwnPort)
            .Distinct()
            .OrderBy(p => p)
            .ToArray();

    private async Task ProbeAsync(CancellationToken ct)
    {
        int[] ports;
        lock (_gate)
            ports = CandidatePorts().Where(p => !(_peers.TryGetValue(p, out var l) && l.Live)).ToArray();
        if (ports.Length == 0)
            return;

        var replies = await Task.WhenAll(ports.Select(async port =>
            (Port: port, Reply: await GetAsync(port, "/coop/info", _options.ProbeTimeout, ct))));

        foreach (var (port, reply) in replies)
        {
            if (reply.Json is not { } info)
            {
                // Nothing listening is the usual case (solo play), so it isn't logged.
                if (reply.Status != 0)
                    LogOnce(port, $"port {port} answered /coop/info with HTTP {reply.Status} (not a SpireMind with co-op); ignored");
                continue;
            }
            if (Text(info["name"]) != "spiremind" || Text(info["session"]) is not { Length: > 0 })
            {
                LogOnce(port, $"port {port} isn't a SpireMind bridge with co-op; ignored");
                continue;
            }
            if (Text(info["role"]) != "bot")
            {
                LogOnce(port, $"port {port} is a SpireMind copy with role {Text(info["role"]) ?? "unknown"}, not a bot; ignored");
                continue;
            }
            if (!AddLink(port, info))
                LogOnce(port, $"a bot copy on port {port} answered, but {_options.MaxBots} are linked already; ignored");
        }
    }

    private void LogOnce(int port, string message)
    {
        bool first;
        lock (_gate) first = _ignored.Add(port);
        if (first) _log(message);
    }

    // Links a bot copy that answered /coop/info: first contact reads from its current line, a
    // known copy that came back reads on from where it was, and a restarted one from its start.
    // False when MaxBots are linked already.
    private bool AddLink(int port, JsonObject info)
    {
        var session = Text(info["session"])!;
        var lastSeq = Long(info["last_seq"]) ?? 0;
        Link link;
        string announcement;
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return true;
            if (_peers.Values.Count(l => l.Live) >= _options.MaxBots)
                return false;
            _ignored.Remove(port);

            _peers.TryGetValue(port, out var old);
            long since;
            if (old is null) since = lastSeq;
            else if (old.Session == session) since = Math.Min(old.Since, lastSeq);
            else since = 0;

            link = new Link(port, session, since, CancellationTokenSource.CreateLinkedTokenSource(_stopToken))
            {
                Character = old?.Character,
                NetId = old?.NetId,
            };
            UpdateIdentity(link, info);
            _peers[port] = link;
            Volatile.Write(ref _liveCount, _peers.Values.Count(l => l.Live));

            var label = LabelLocked(link);
            announcement = old is null ? $"{label} joined the chat (port {port})."
                : old.Session == session ? $"{label}'s copy is answering again."
                : $"{label}'s copy restarted.";
        }

        _log($"linked the bot copy on port {port} (session {session}, reading after line {link.Since})");
        RefreshHelpNote();
        Announce(announcement);
        SyncCloud(link);
        FeedConductor(link);
        _ = Task.Run(() => WorkAsync(link));
        return true;
    }

    // Takes a link off: its port goes back to probing. Commands still waiting get a line each.
    private void Drop(Link link, string? announcement)
    {
        string label;
        lock (_gate)
        {
            if (!link.Live)
                return;
            link.Live = false;
            Volatile.Write(ref _liveCount, _peers.Values.Count(l => l.Live));
            label = LabelLocked(link);
        }
        try { link.Cancel.Cancel(); }
        catch (ObjectDisposedException) { /* already gone */ }

        _log($"dropped the bot copy on port {link.Port}");
        _conductor.Gone(link.Port);
        SyncCloud(link);
        RefreshHelpNote();
        while (link.Jobs.TryDequeue(out var job))
        {
            if (job.Kind is JobKind.Command or JobKind.Quit)
                Announce($"{label} didn't answer (its copy may have closed).");
        }
        if (announcement is not null)
            Announce(announcement);
    }

    // ---- One worker per bot copy ----

    private async Task WorkAsync(Link link)
    {
        var ct = link.Cancel.Token;
        var sincePoll = Stopwatch.StartNew();
        var pollNow = true;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (pollNow || sincePoll.Elapsed >= _options.PollInterval)
                {
                    var polled = await PollAsync(link, ct);
                    sincePoll.Restart();
                    pollNow = polled == Polled.Again;
                    if (polled == Polled.Down)
                    {
                        Drop(link, GoneLine(link));
                        return;
                    }
                    if (polled == Polled.NotABot)
                    {
                        Drop(link, null);
                        return;
                    }
                    continue;
                }

                var wait = _options.PollInterval - sincePoll.Elapsed;
                if (await link.Signal.WaitAsync(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, ct)
                    && link.Jobs.TryDequeue(out var job))
                {
                    await PostAsync(link, job, ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Dropped or stopped.
        }
        catch (Exception ex)
        {
            _log($"the worker for port {link.Port} failed: {ex}");
            Drop(link, GoneLine(link));
        }
        finally
        {
            // The worker only ends once the link is dropped or the relay stops, so the link is
            // done with: nothing may queue on it now, and its token source (registered on the
            // relay's) and semaphore are released, so a copy that keeps dropping and relinking
            // leaves nothing behind.
            lock (_gate)
            {
                if (link.Live)
                {
                    link.Live = false;
                    Volatile.Write(ref _liveCount, _peers.Values.Count(l => l.Live));
                }
            }
            // A cloud still up over a bot whose worker ended (the relay stopping) comes down,
            // unless the relay is gone, when the game is quitting anyway.
            if (Volatile.Read(ref _disposed) == 0)
                SyncCloud(link);
            link.Cancel.Dispose();
            link.Signal.Dispose();
        }
    }

    private async Task<Polled> PollAsync(Link link, CancellationToken ct)
    {
        var reply = await GetAsync(link.Port, $"/chat?since={link.Since.ToString(CultureInfo.InvariantCulture)}", _options.PollTimeout, ct);
        if (reply.Json is not { } log || Text(log["session"]) is not { Length: > 0 } session)
        {
            link.Failures++;
            return link.Failures >= Math.Max(1, _options.DownAfterFailures) ? Polled.Down : Polled.Failed;
        }
        link.Failures = 0;

        if (Text(log["role"]) is { } role && role != "bot")
        {
            _log($"port {link.Port} no longer answers as a bot copy (role {role})");
            return Polled.NotABot;
        }

        if (session != link.Session)
        {
            // A new process on the same port: its log starts again at line 1.
            string label;
            lock (_gate)
            {
                link.Session = session;
                UpdateIdentity(link, log);
                label = LabelLocked(link);
            }
            link.Since = 0;
            link.Skip.Clear();
            Announce($"{label}'s copy restarted.");
            SyncCloud(link);
            FeedConductor(link);
            return Polled.Again;
        }

        if (Long(log["last_seq"]) is { } last && last < link.Since)
            link.Since = last;
        lock (_gate) UpdateIdentity(link, log);
        SyncCloud(link);
        FeedConductor(link);

        var lines = (log["lines"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(l => (Seq: Long(l["seq"]) ?? 0, Line: l))
            .Where(l => l.Seq > link.Since)
            .OrderBy(l => l.Seq)
            .ToArray();

        foreach (var (seq, line) in lines)
        {
            link.Since = Math.Max(link.Since, seq);
            // Lines relayed into this bot (Mark's, other bots') and replies already shown.
            if (Bool(line["remote"]) || Text(line["kind"]) == "human" || link.Skip.Remove(seq))
                continue;
            var text = Text(line["text"]);
            if (string.IsNullOrWhiteSpace(text))
                continue;
            switch (Text(line["kind"]))
            {
                case "bot":
                    ShowBotLine(link, Text(line["from"]), text);
                    break;
                case "system":
                    AnnounceFrom(Label(link), text);
                    break;
            }
        }
        link.Skip.RemoveWhere(s => s <= link.Since);
        return Polled.Ok;
    }

    // A bot's own line: into the human's log, as a bubble over its player, and to the other bots.
    private void ShowBotLine(Link link, string? from, string text)
    {
        string label;
        string? character;
        var known = KnownBots();
        lock (_gate)
        {
            label = LabelLocked(link, known);
            character = CharacterLocked(link, known);
        }
        // A bot signs its lines with its character; the label tells two of one character apart.
        var who = string.IsNullOrWhiteSpace(from) || from == "Bot"
                  || (character is not null && string.Equals(CleanCharacter(from), character, StringComparison.OrdinalIgnoreCase))
            ? label
            : from;
        ChatLine shown;
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            shown = _chat.AddRemoteSay(who, text);
        }
        catch (ArgumentException)
        {
            return; // nothing to show
        }
        catch (Exception ex)
        {
            _log($"showing a line from port {link.Port} failed: {ex.Message}");
            return;
        }

        if (_coopBots is not null && NetIdFor(link) is { } netId)
        {
            try { _coopBots.ShowLine(netId, shown.Text); }
            catch (Exception ex) { _log($"showing a bubble for port {link.Port} failed: {ex.Message}"); }
        }

        if (_partyChat)
            PassToParty(link, label, shown.Text);
    }

    // Party chat: the line goes to every other bot as a teammate message, at most
    // PartyLinesPerMinute per source bot. Teammate messages never wake a bot, and the receiving
    // bot logs them as remote lines, which are never passed on again, so nothing can loop.
    private void PassToParty(Link source, string label, string text)
    {
        var body = new JsonObject { ["from"] = label, ["text"] = text, ["teammate"] = true }.ToJsonString();
        string? overflow = null;
        lock (_gate)
        {
            var others = _peers.Values.Where(l => l.Live && l != source).ToArray();
            if (others.Length == 0)
                return;
            var now = _clock();
            while (source.PartyTimes.Count > 0 && now - source.PartyTimes.Peek() >= PartyWindow)
                source.PartyTimes.Dequeue();
            if (source.PartyTimes.Count >= _options.PartyLinesPerMinute)
            {
                if (now - source.PartyOverflowLogged >= PartyWindow)
                {
                    source.PartyOverflowLogged = now;
                    overflow = $"party chat: {label} said more than {_options.PartyLinesPerMinute} lines in a minute; the rest aren't passed on for now";
                }
            }
            else
            {
                source.PartyTimes.Enqueue(now);
                foreach (var other in others)
                    EnqueueLocked(other, new Job(JobKind.Party, body));
            }
        }
        if (overflow is not null) _log(overflow);
    }

    private async Task PostAsync(Link link, Job job, CancellationToken ct)
    {
        var reply = await PostJsonAsync(link.Port, job.Path, job.Body, _options.PostTimeout, ct);
        switch (job.Kind)
        {
            case JobKind.Line or JobKind.Party:
                if (reply is null)
                    _log($"couldn't pass a {(job.Kind == JobKind.Party ? "party" : "chat")} line to the bot copy on port {link.Port}");
                return;
            case JobKind.Nudge:
                if (reply is null)
                    _log($"couldn't ask the bot copy on port {link.Port} to look for the lobby now");
                return;
            case JobKind.PartyOp:
                if (reply is null)
                    _log($"party: couldn't send an op to the bot copy on port {link.Port}");
                else if (reply["ok"] is JsonValue ok && ok.TryGetValue<bool>(out var good) && !good)
                    _log($"party: the bot copy on port {link.Port} turned an op down: {Text(reply["error"]) ?? "no reason given"}");
                return;
            case JobKind.Quit:
                var who = Label(link);
                if (reply is null)
                {
                    Announce($"{who} didn't take the request to quit (its copy may have closed already).");
                    return;
                }
                link.Quitting = true;
                AnnounceFrom(who, Text(reply["reply"]) is { } said && !string.IsNullOrWhiteSpace(said) ? said : "Leaving the game and quitting.");
                return;
        }

        var label = Label(link);
        if (reply is null)
        {
            Announce($"{label} didn't answer (its copy may have closed).");
            return;
        }
        // The poll that comes next must not show the reply line again.
        if (Long(reply["seq"]) is { } seq)
            link.Skip.Add(seq);
        if (Text(reply["reply"]) is { } answer && !string.IsNullOrWhiteSpace(answer))
            AnnounceFrom(label, answer);
    }

    // Caller holds _gate. Hands a job to the link's worker.
    private static void EnqueueLocked(Link link, Job job)
    {
        if (!link.Live)
            return;
        link.Jobs.Enqueue(job);
        link.Signal.Release();
    }

    // ---- The human's lines and commands ----

    // Mark's own lines (typed in his window or posted to his /chat). In co-op they go to every
    // bot, and each bot decides by name whether it is for it. Outside co-op a line goes only to
    // the linked bot whose name it starts with ("Silent, ready?"), as this copy's own bot is the
    // one he talks to there. A line that starts with a bot's label ("Ironclad 2, block") skips
    // the other bots on that character, who would each take a line starting with their
    // character as theirs.
    private void OnLine(ChatLine line)
    {
        if (line.Kind != ChatLineKind.Human || line.Remote)
            return;
        // A planning session takes his lines into the bots' briefs and the plan.
        _conductor.OnHumanLine(line.From, line.Text);
        var inCoop = InCoop;
        var body = new JsonObject { ["from"] = line.From, ["text"] = line.Text }.ToJsonString();
        var known = KnownBots();
        lock (_gate)
        {
            var labels = LabelsLocked(known);
            var (target, _) = MatchLabel(line.Text, labels);
            var named = target is null && !inCoop && TargetWord().Match(line.Text) is { Success: true } m ? m.Groups["word"].Value : null;
            var targetCharacter = target is null ? null : CharacterLocked(target, known);
            foreach (var link in labels.Keys)
            {
                bool send;
                if (target is not null)
                    send = link == target || (inCoop && !string.Equals(CharacterLocked(link, known) ?? "", targetCharacter ?? "-", StringComparison.OrdinalIgnoreCase));
                else
                    send = inCoop || (named is not null && string.Equals(CharacterLocked(link, known), named, StringComparison.OrdinalIgnoreCase));
                if (send)
                    EnqueueLocked(link, new Job(JobKind.Line, body));
            }
        }
        // This copy's brain doesn't play in co-op, where the bots answer, so nothing should
        // pile up for it.
        if (inCoop && BotsExist)
            _chat.TakeUnread();
    }

    // ChatHub.CommandFilter: in co-op the bots' commands go to the bots while any exist; in co-op
    // or not, one that names a linked bot first goes to that bot. Never waits: it only queues
    // the POSTs, and each bot's answer is shown when it comes.
    private string? Filter(string from, string name, string args)
    {
        RefreshHelpNote();
        var command = name.ToLowerInvariant();
        var help = command == "help";
        if (!help && !ForwardedCommands.Contains(command))
            return null;
        if (!BotsExist)
            return null;

        var inCoop = InCoop;
        var known = KnownBots();
        lock (_gate)
        {
            var live = _peers.Values.Where(l => l.Live).OrderBy(l => l.Port).ToList();
            var (target, rest, missing) = ResolveTargetLocked(args, live, known);

            List<Link> targets;
            if (target is null && !inCoop)
            {
                return null; // solo play: this copy's own command
            }
            else if (target is not null)
            {
                targets = [target];
                args = rest;
            }
            else if (help && missing is null)
            {
                return null; // this copy's /help, which ends with the co-op note
            }
            else if (live.Count == 0)
            {
                return NotAnsweringYet;
            }
            else if (missing is not null)
            {
                return $"No bot is playing {missing} right now. /bots lists them.";
            }
            else
            {
                targets = live;
            }

            var body = new JsonObject
            {
                ["from"] = from,
                ["text"] = "/" + name + (args.Length > 0 ? " " + args : ""),
            }.ToJsonString();
            foreach (var link in targets)
                EnqueueLocked(link, new Job(JobKind.Command, body));
        }
        return "";
    }

    // Caller holds _gate. The bot a command's arguments name first, by its label ("Ironclad 2")
    // or else its character, and the arguments after the name. Missing is the proper name of
    // a character the arguments start with that no live bot plays; both null when the
    // arguments don't start with a name.
    private (Link? Target, string After, string? Missing) ResolveTargetLocked(string args, List<Link> live,
        IReadOnlyList<(int Port, ulong NetId, string Character)> known)
    {
        var (target, rest) = MatchLabel(args, LabelsLocked(known));
        if (target is not null)
            return (target, rest, null);

        var match = TargetWord().Match(args);
        if (!match.Success || CharacterName(match.Groups["word"].Value, live) is not { } character)
            return (null, args, null);
        target = live.Find(l => string.Equals(CharacterLocked(l, known), character, StringComparison.OrdinalIgnoreCase));
        return target is null ? (null, args, character) : (target, match.Groups["rest"].Value.Trim(), null);
    }

    // The proper name of a character ("silent" gives "Silent"), from the chat's names and the
    // linked bots' characters, or null when the word isn't one. "Random" isn't a character.
    private string? CharacterName(string word, IEnumerable<Link> live) =>
        _chat.KnownNames
            .Concat(CharacterChoices.Where(c => c != "Random"))
            .Concat(live.Select(l => l.Character).OfType<string>())
            .FirstOrDefault(n => n.Equals(word, StringComparison.OrdinalIgnoreCase));

    // /bots: one line about every bot copy, with where it stands when it says.
    private string ListBots(string from, string args)
    {
        var parts = new List<string>();
        var known = KnownBots();
        HashSet<int> linked, down;
        lock (_gate)
        {
            var labels = LabelsLocked(known);
            var live = labels.Keys.OrderBy(l => l.Port).ToArray();
            linked = live.Select(l => l.Port).ToHashSet();
            down = _peers.Values.Where(l => !l.Live).Select(l => l.Port).ToHashSet();
            foreach (var link in live)
            {
                var details = new List<string>();
                if (NetIdLocked(link, known) is { } netId) details.Add($"NetId {netId}");
                switch (link.Playing)
                {
                    case "api": details.Add("playing on API credit"); break;
                    case "subscription": details.Add("playing on the subscription"); break;
                    case "none": details.Add("not playing"); break;
                }
                if (link.Paused) details.Add("paused");
                if (link.Thinking) details.Add("deciding");
                var head = details.Count > 0 ? $"{labels[link]} on {link.Port} ({string.Join(", ", details)})" : $"{labels[link]} on {link.Port}";
                parts.Add(StatusText(link.Status) is { } where ? $"{head}: {where}." : $"{head}.");
            }
        }
        // The game's copies that haven't answered yet, or stopped answering.
        foreach (var k in known.Where(k => !linked.Contains(k.Port) && k.Port != _options.OwnPort).OrderBy(k => k.Port))
            parts.Add($"{CleanCharacter(k.Character) ?? "A bot"} on {k.Port} ({(down.Contains(k.Port) ? "not answering" : "starting")}).");

        var starting = Volatile.Read(ref _starting) != 0;
        if (parts.Count == 0)
            return starting ? "A bot copy is starting." : "No bot copies are running. /bot add [character] starts one.";
        if (starting)
            parts.Add("Another bot copy is starting.");
        return string.Join("\n", parts);
    }

    // Where a bot copy says it stands, for /bots: "in the run", or the lobby driver's line
    // ("joined; picked Silent; ready; waiting for the host to start"). Null when it says nothing.
    private static string? StatusText(BotStatus? status)
    {
        if (status is null)
            return null;
        if (status.InRun)
            return "in the run";
        var line = status.Line is { } l ? Whitespace().Replace(l, " ").Trim().TrimEnd('.') : "";
        if (line.Length > MaxFailureLength)
            line = line[..(MaxFailureLength - 3)].TrimEnd() + "...";
        if (status.JoinStopped && !line.StartsWith("stopped", StringComparison.OrdinalIgnoreCase))
            return line.Length > 0 ? $"stopped trying to join ({line})" : "stopped trying to join";
        return line.Length > 0 ? line : null;
    }

    // /bot add [character]: starts a bot copy through the game. /bot stop [name]: asks one bot
    // copy, or every one, to leave and quit.
    private string BotCommand(string from, string args)
    {
        var words = args.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length > 0 && words[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
            return StopCommand(from, words.Length > 1 ? words[1] : null);

        var match = BotAdd().Match(args);
        if (!match.Success)
            return BotUsage;
        string? choice = null;
        if (match.Groups["who"].Success)
        {
            choice = CharacterChoices.FirstOrDefault(c => c.Equals(match.Groups["who"].Value, StringComparison.OrdinalIgnoreCase));
            if (choice is null)
                return BotUsage;
        }
        if (_coopBots is not { } coopBots)
            return NoLauncher;

        var known = KnownBots();
        int bots;
        lock (_gate)
            bots = _peers.Values.Where(l => l.Live).Select(l => l.Port).Concat(known.Select(k => k.Port)).Distinct().Count();
        if (bots >= _options.MaxBots)
            return _options.MaxBots == 1 ? "One bot copy is the most." : $"{NumberWord(_options.MaxBots)} bot copies is the most.";
        if (Interlocked.CompareExchange(ref _starting, 1, 0) != 0)
            return "A bot copy is already starting.";

        _ = Task.Run(() => StartCopyAsync(coopBots, choice));
        return choice is null ? "Starting a bot copy..." : $"Starting a bot copy as {choice}...";
    }

    // The game side's start, given StartTimeout: one that never ends (a copy that can't join)
    // must not keep /bot add refusing for the rest of the session.
    private async Task StartCopyAsync(ICoopBots coopBots, string? choice)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stopToken);
        var timeout = _options.StartTimeout > TimeSpan.Zero ? _options.StartTimeout : CoopRelayOptions.DefaultStartTimeout;
        try
        {
            deadline.CancelAfter(timeout);
            var line = await coopBots.StartCopyAsync(choice, deadline.Token).WaitAsync(timeout, _stopToken);
            if (!string.IsNullOrWhiteSpace(line))
                Announce(line);
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested)
        {
            // The relay is stopping.
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && deadline.IsCancellationRequested))
        {
            _log($"/bot add: the game side's start didn't finish within {timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s");
            Announce($"The bot copy didn't come up within {Duration(timeout)}. /bots shows what is running; /bot add tries again.");
        }
        catch (Exception ex)
        {
            Announce($"Couldn't start a bot copy: {Describe(ex)}");
        }
        finally
        {
            Volatile.Write(ref _starting, 0);
            // Look for the new copy now rather than at the next round.
            try { _wake.Release(); }
            catch (ObjectDisposedException) { /* stopping */ }
        }
    }

    // /bot stop [name]. With a name, that bot copy is asked through its bridge to leave the
    // lobby or run and quit. Without one, the game stops every copy it started, and the ones it
    // didn't start (scripts/coop-bot.sh) are asked through their bridges. Never waits.
    private string StopCommand(string from, string? who)
    {
        var body = new JsonObject { ["from"] = from }.ToJsonString();
        var known = KnownBots();
        var coopBots = _coopBots;
        if (who is null)
        {
            var byGame = coopBots is not null && known.Any(k => k.Port != _options.OwnPort);
            var gamePorts = known.Select(k => k.Port).ToHashSet();
            var asked = 0;
            lock (_gate)
            {
                foreach (var link in _peers.Values.Where(l => l.Live))
                {
                    if (byGame && gamePorts.Contains(link.Port))
                        continue;
                    EnqueueLocked(link, new Job(JobKind.Quit, body, QuitPath));
                    asked++;
                }
            }
            if (byGame)
                _ = Task.Run(() => StopAllAsync(coopBots!));
            else if (asked == 0)
                return "No bot copies are running.";
            return "Asking every bot copy to leave and quit...";
        }

        var name = CleanCharacter(who) ?? who;
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase) || name.Equals("everyone", StringComparison.OrdinalIgnoreCase))
            return StopCommand(from, null);
        lock (_gate)
        {
            var labels = LabelsLocked(known);
            var (target, rest) = MatchLabel(name, labels);
            if (target is null || rest.Length > 0)
            {
                // Not a whole label ("Ironclad 3" where only "Ironclad" plays): the first bot on
                // that character, if the name is one.
                target = labels.Keys.OrderBy(l => l.Port)
                    .FirstOrDefault(l => string.Equals(CharacterLocked(l, known), name, StringComparison.OrdinalIgnoreCase));
            }
            if (target is not null)
            {
                EnqueueLocked(target, new Job(JobKind.Quit, body, QuitPath));
                return $"Asking {labels[target]} to leave and quit...";
            }
        }
        if (known.Any(k => k.Port != _options.OwnPort && string.Equals(CleanCharacter(k.Character), name, StringComparison.OrdinalIgnoreCase)))
            return $"{name} isn't answering, so it can't be asked by name. /bot stop with no name stops every bot copy this game started.";
        return $"No bot is called {name}. /bots lists them.";
    }

    // The game side's stop of every copy it started, given StopTimeout.
    private async Task StopAllAsync(ICoopBots coopBots)
    {
        try
        {
            var line = await coopBots.StopAllAsync(_stopToken).WaitAsync(StopTimeout, _stopToken);
            if (!string.IsNullOrWhiteSpace(line))
                Announce(line);
        }
        catch (OperationCanceledException) when (_stopToken.IsCancellationRequested)
        {
            // The relay is stopping.
        }
        catch (TimeoutException)
        {
            _log($"/bot stop: the game side's stop didn't finish within {StopTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s");
            Announce("The bot copies didn't all quit in time. /bots shows what is still running.");
        }
        catch (Exception ex)
        {
            Announce($"Couldn't stop the bot copies: {Describe(ex)}");
        }
    }

    /// <summary>
    /// Asks every linked bot copy, and every one the game started that isn't linked yet, to look
    /// for the host's lobby now (POST /coop/join-now) instead of at its next retry. The game side
    /// calls it when the human's copy enters a co-op lobby. Returns at once; any thread.
    /// </summary>
    public void NudgeBots()
    {
        if (Volatile.Read(ref _disposed) != 0 || Volatile.Read(ref _started) == 0)
            return;
        var known = KnownBots();
        int linked;
        int[] unlinked;
        lock (_gate)
        {
            var live = _peers.Values.Where(l => l.Live).ToList();
            foreach (var link in live)
                EnqueueLocked(link, new Job(JobKind.Nudge, "{}", JoinNowPath));
            linked = live.Count;
            unlinked = known.Select(k => k.Port)
                .Where(p => p is >= 1 and <= 65535 && p != _options.OwnPort && live.All(l => l.Port != p))
                .Distinct()
                .ToArray();
        }
        foreach (var port in unlinked)
            _ = Task.Run(() => NudgeUnlinkedAsync(port));
        if (linked + unlinked.Length > 0)
            _log($"a co-op lobby opened here: asked {linked + unlinked.Length} bot {(linked + unlinked.Length == 1 ? "copy" : "copies")} to look for it now");
    }

    // A copy the game started that hasn't answered /coop/info yet (it may still be loading):
    // one quiet try, since it looks for the lobby by itself anyway.
    private async Task NudgeUnlinkedAsync(int port)
    {
        try
        {
            await PostJsonAsync(port, JoinNowPath, "{}", _options.ProbeTimeout, _stopToken, quiet: true);
        }
        catch (OperationCanceledException)
        {
            // The relay is stopping.
        }
        catch (Exception ex)
        {
            _log($"asking port {port} to look for the lobby failed: {ex.Message}");
        }
    }

    // /party [on|off]
    private string PartyCommand(string from, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                break;
            case "on":
                PartyChat = true;
                break;
            case "off":
                PartyChat = false;
                break;
            default:
                return "Use /party on or /party off.";
        }
        return PartyChat
            ? "Party chat is on: the bots hear each other's lines."
            : "Party chat is off: the bots hear only you.";
    }

    // ---- Small helpers ----

    // The /help note follows BotsExist.
    private void RefreshHelpNote()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        var note = BotsExist ? CoopHelpNote : null;
        if (_chat.HelpNote != note)
            _chat.HelpNote = note;
    }

    // A line in the human's log. A failing LineAdded handler is logged, never thrown into a
    // worker (which would drop the bot).
    private void Announce(string text)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _chat.Announce(text); }
        catch (ArgumentException) { /* nothing to show */ }
        catch (Exception ex) { _log($"showing a line failed: {ex.Message}"); }
    }

    private void AnnounceFrom(string who, string text)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _chat.AnnounceFrom(who, text); }
        catch (ArgumentException) { /* nothing to show */ }
        catch (Exception ex) { _log($"showing a line from {who} failed: {ex.Message}"); }
    }

    private IReadOnlyList<(int Port, ulong NetId, string Character)> KnownBots()
    {
        try
        {
            return _coopBots?.Known ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private string Label(Link link)
    {
        var known = KnownBots();
        lock (_gate) return LabelLocked(link, known);
    }

    // Caller holds _gate. What the chat calls the bot (see LabelsLocked).
    private string LabelLocked(Link link) => LabelLocked(link, KnownBots());

    private string LabelLocked(Link link, IReadOnlyList<(int Port, ulong NetId, string Character)> known) =>
        link.Live && LabelsLocked(known).TryGetValue(link, out var label)
            ? label
            : NameplateLocked(link, known) ?? CharacterLocked(link, known) ?? $"The bot on {link.Port}";

    // Caller holds _gate. What the chat calls each live bot: its nameplate on the human's screen
    // in a local co-op game ("Ironclad 2"), else its character, numbered from the second live
    // bot on one character by port, else "The bot on 17773". Never two the same.
    private Dictionary<Link, string> LabelsLocked(IReadOnlyList<(int Port, ulong NetId, string Character)> known)
    {
        var live = _peers.Values.Where(l => l.Live).OrderBy(l => l.Port).ToList();
        var labels = new Dictionary<Link, string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in live)
        {
            if (NameplateLocked(link, known) is { } plate && taken.Add(plate))
                labels[link] = plate;
        }
        foreach (var link in live)
        {
            if (labels.ContainsKey(link))
                continue;
            if (CharacterLocked(link, known) is not { } character)
            {
                labels[link] = $"The bot on {link.Port}";
                continue;
            }
            var name = character;
            for (var n = 2; !taken.Add(name); n++)
                name = $"{character} {n.ToString(CultureInfo.InvariantCulture)}";
            labels[link] = name;
        }
        return labels;
    }

    // Caller holds _gate. The bot's character as it reported it, else as the game started it.
    private static string? CharacterLocked(Link link, IReadOnlyList<(int Port, ulong NetId, string Character)> known) =>
        link.Character ?? KnownCharacter(link.Port, known);

    // Caller holds _gate. The bot's nameplate on the human's screen, or null outside a local
    // co-op game, before its NetId is known, or without the game side.
    private string? NameplateLocked(Link link, IReadOnlyList<(int Port, ulong NetId, string Character)> known)
    {
        if (_coopBots is null || NetIdLocked(link, known) is not { } netId)
            return null;
        try
        {
            return _coopBots.NameOf(netId) is { } name ? CleanCharacter(Whitespace().Replace(name, " ")) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The live bot whose label the text starts with (after an optional "the"), the longest label
    // winning ("Ironclad 2" over "Ironclad"), and the text after the name; else (null, text).
    private static (Link? Link, string After) MatchLabel(string text, Dictionary<Link, string> labels)
    {
        Link? best = null;
        var bestLength = -1;
        var rest = text;
        foreach (var (link, label) in labels)
        {
            if (label.Length > bestLength && StartsWithName(text, label, out var after))
            {
                best = link;
                bestLength = label.Length;
                rest = after;
            }
        }
        return (best, best is null ? text : rest);
    }

    // True when the text starts with the name (any case, after an optional "the") followed by
    // its end, a space or punctuation; rest is what follows, without the punctuation.
    private static bool StartsWithName(string text, string name, out string rest)
    {
        rest = text;
        var t = text.TrimStart();
        if (!name.StartsWith("the ", StringComparison.OrdinalIgnoreCase)
            && t.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
            t = t[4..].TrimStart();
        if (name.Length == 0 || !t.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            return false;
        var after = t[name.Length..];
        if (after.Length > 0 && !char.IsWhiteSpace(after[0]) && !NamePunctuation.Contains(after[0]))
            return false;
        rest = after.TrimStart(NamePunctuation).Trim();
        return true;
    }

    private static readonly char[] NamePunctuation = [',', ':', ';', '.', '!', '?'];

    // What the chat says when a bot copy's polls fail for good.
    private string GoneLine(Link link) =>
        link.Quitting ? $"{Label(link)} left, and its copy has quit." : $"{Label(link)}'s copy stopped answering.";

    // Shows or takes away the thought cloud over the bot's player on the human's screen, to
    // match what its copy last said (a cloud only while it is linked and deciding). Called with
    // no lock held, after every identity update and when the link goes; the game's side is only
    // called on a change. A failure is logged once.
    private void SyncCloud(Link link)
    {
        if (_coopBots is not { } coopBots)
            return;
        var known = KnownBots();
        ulong? off = null, on = null;
        lock (_gate)
        {
            var netId = NetIdLocked(link, known);
            var want = link.Live && link.Thinking && netId is not null && Volatile.Read(ref _disposed) == 0;
            if (link.CloudNetId is { } shown && (!want || shown != netId))
            {
                off = shown;
                link.CloudNetId = null;
            }
            if (want && link.CloudNetId is null)
            {
                on = netId;
                link.CloudNetId = netId;
            }
        }
        try
        {
            if (off is { } a) coopBots.Thinking(a, false);
            if (on is { } b) coopBots.Thinking(b, true);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _cloudFailed, 1) == 0)
                _log($"showing a bot's thought cloud failed: {ex.Message}");
        }
    }

    private ulong? NetIdFor(Link link)
    {
        var known = KnownBots();
        lock (_gate) return NetIdLocked(link, known);
    }

    // Caller holds _gate. The link's NetId, else the one the game gave for that port.
    private static ulong? NetIdLocked(Link link, IReadOnlyList<(int Port, ulong NetId, string Character)> known)
    {
        if (link.NetId is { } netId) return netId;
        foreach (var k in known)
        {
            if (k.Port == link.Port && k.NetId != 0) return k.NetId;
        }
        return null;
    }

    private static string? KnownCharacter(int port, IReadOnlyList<(int Port, ulong NetId, string Character)> known)
    {
        foreach (var k in known)
        {
            if (k.Port == port && CleanCharacter(k.Character) is { } name) return name;
        }
        return null;
    }

    private static string? CleanCharacter(string? name)
    {
        var clean = name?.Trim();
        if (clean is not null && clean.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            clean = clean[4..].TrimStart();
        return string.IsNullOrEmpty(clean) ? null : clean;
    }

    // Caller holds _gate. What a /coop/info or /chat reply says about the bot. A character or
    // NetId it no longer reports (between runs) keeps the last one, so labels stay put.
    private static void UpdateIdentity(Link link, JsonObject json)
    {
        link.Status = ReadStatus(json["status"] as JsonObject);
        if (CleanCharacter(Text(json["character"])) is { } character) link.Character = character;
        else if (NiceCharacter(link.Status?.Character) is { } held) link.Character = held;
        if (ULong(json["net_id"]) is { } netId) link.NetId = netId;
        link.Playing = Text(json["playing"]);
        link.Paused = Bool(json["paused"]);
        link.Thinking = Bool(json["thinking"]);
        link.Party = json["party"] is JsonObject party ? (JsonObject)party.DeepClone() : null;
    }

    // Hands what the bot last reported to the planning sessions' conductor, with no lock held.
    private void FeedConductor(Link link)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        var known = KnownBots();
        string label, session;
        string? playing;
        bool paused, live;
        bool? inRun;
        JsonObject? party;
        lock (_gate)
        {
            live = link.Live;
            label = LabelLocked(link, known);
            session = link.Session;
            playing = link.Playing;
            paused = link.Paused;
            inRun = link.Status?.InRun;
            party = link.Party;
        }
        if (!live)
            return;
        try { _conductor.Observe(link.Port, label, session, playing, paused, inRun, party); }
        catch (Exception ex) { _log($"party: reading port {link.Port} failed: {ex.Message}"); }
    }

    // The conductor's op for one bot, queued on that bot's worker. Never waits.
    private void SendPartyOp(int port, JsonObject op)
    {
        var body = op.ToJsonString();
        lock (_gate)
        {
            if (_peers.TryGetValue(port, out var link))
                EnqueueLocked(link, new Job(JobKind.PartyOp, body, PartyPath));
        }
    }

    // The "status" object a bot copy reports, or null.
    private static BotStatus? ReadStatus(JsonObject? json) =>
        json is null ? null : new BotStatus(Text(json["line"]), Text(json["character"]), Bool(json["in_run"]), Bool(json["join_stopped"]));

    // The game's id of a character as the chat names it: "SILENT" gives "Silent".
    private static string? NiceCharacter(string? id)
    {
        var clean = CleanCharacter(id);
        if (clean is null)
            return null;
        if (clean.Any(char.IsLower))
            return clean;
        var lower = clean.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    // "3 minutes", "90 seconds", "1 second".
    private static string Duration(TimeSpan span)
    {
        var seconds = Math.Max(1, (long)Math.Round(span.TotalSeconds));
        if (seconds >= 120 && seconds % 60 == 0)
            return $"{(seconds / 60).ToString(CultureInfo.InvariantCulture)} minutes";
        return seconds == 1 ? "1 second" : $"{seconds.ToString(CultureInfo.InvariantCulture)} seconds";
    }

    private static string NumberWord(int n) => n switch
    {
        2 => "Two", 3 => "Three", 4 => "Four", 5 => "Five", 6 => "Six", 7 => "Seven", 8 => "Eight", 9 => "Nine",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };

    private static string Describe(Exception ex)
    {
        var why = Whitespace().Replace(ex.Message, " ").Trim();
        if (why.Length > MaxFailureLength)
            why = why[..(MaxFailureLength - 3)].TrimEnd() + "...";
        return why.Length > 0 ? why : ex.GetType().Name;
    }

    // ---- HTTP, loopback only ----

    private static Uri Url(int port, string pathAndQuery)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "Not a port.");
        return new Uri("http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + pathAndQuery);
    }

    // A GET's status and JSON object body (null unless 2xx with an object). Status 0 means
    // nothing answered in time. Throws only when ct (the relay or link stopping) is cancelled.
    private async Task<(int Status, JsonObject? Json)> GetAsync(int port, string pathAndQuery, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var status = 0;
        try
        {
            using var res = await _http.GetAsync(Url(port, pathAndQuery), deadline.Token);
            status = (int)res.StatusCode;
            if (!res.IsSuccessStatusCode)
                return (status, null);
            var body = await res.Content.ReadAsStringAsync(deadline.Token);
            return (status, JsonNode.Parse(body) as JsonObject);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException
                                       or ObjectDisposedException or InvalidOperationException or IOException)
        {
            // A body that isn't JSON keeps its status (something else is on the port).
            return (ex is JsonException ? status : 0, null);
        }
    }

    // A POST of a JSON body; the reply object when the status is 2xx, else null (logged unless quiet).
    private async Task<JsonObject?> PostJsonAsync(int port, string path, string body, TimeSpan timeout, CancellationToken ct,
        bool quiet = false)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var res = await _http.PostAsync(Url(port, path), content, deadline.Token);
            if (!res.IsSuccessStatusCode)
            {
                if (!quiet) _log($"POST {path} on port {port} answered HTTP {(int)res.StatusCode}");
                return null;
            }
            return JsonNode.Parse(await res.Content.ReadAsStringAsync(deadline.Token)) as JsonObject ?? new JsonObject();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException
                                       or ObjectDisposedException or InvalidOperationException or IOException)
        {
            if (!quiet) _log($"POST {path} on port {port} failed: {Describe(ex)}");
            return null;
        }
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long? Long(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<long>(out var n) ? n : null;

    private static ulong? ULong(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<ulong>(out var n) ? n : null;

    private static bool Bool(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    // "Silent high", "the Silent, high": a first word (after an optional "the") and the rest.
    [GeneratedRegex(@"^\s*(?:the\s+)?(?<word>[^\s,:;.!?]+)[,:;.!?]*(?:\s+(?<rest>.*))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex TargetWord();

    [GeneratedRegex(@"^add(?:\s+(?:the\s+)?(?<who>\S+))?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BotAdd();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private enum Polled { Ok, Failed, Down, Again, NotABot }

    private enum JobKind { Line, Command, Party, Quit, Nudge, PartyOp }

    private sealed record Job(JobKind Kind, string Body, string Path = "/chat");

    // A linked bot copy. Session, the identity fields, CloudNetId and Live change under the
    // relay's gate; Since, Failures and Skip belong to the link's worker alone. Quitting is set
    // once the copy took a /bot stop, so its going is told as a quit.
    private sealed class Link(int port, string session, long since, CancellationTokenSource cancel)
    {
        public int Port { get; } = port;
        public string Session { get; set; } = session;
        public long Since { get; set; } = since;
        public string? Character { get; set; }
        public ulong? NetId { get; set; }
        public string? Playing { get; set; }
        public bool Paused { get; set; }
        public bool Thinking { get; set; }
        public BotStatus? Status { get; set; }
        public JsonObject? Party { get; set; }  // the bot's "party" report (PartyBot.Identity), or null
        public ulong? CloudNetId { get; set; }  // the player a thought cloud is up over, else null
        public volatile bool Quitting;
        public bool Live { get; set; } = true;
        public int Failures { get; set; }
        public HashSet<long> Skip { get; } = [];
        public ConcurrentQueue<Job> Jobs { get; } = new();
        public SemaphoreSlim Signal { get; } = new(0);
        public CancellationTokenSource Cancel { get; } = cancel;
        public Queue<DateTime> PartyTimes { get; } = new();
        public DateTime PartyOverflowLogged { get; set; } = DateTime.MinValue;
    }
}
