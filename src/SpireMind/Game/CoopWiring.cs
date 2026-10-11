using SpireMind.Core.Brain;
// Aliases rather than the namespace, so a game type of the same name in the global usings can
// never make them ambiguous.
using CoopRelay = SpireMind.Core.Coop.CoopRelay;
using CoopRelayOptions = SpireMind.Core.Coop.CoopRelayOptions;

namespace SpireMind.Game;

/// <summary>
/// Starts the co-op chat relay in the human's copy (docs/chat.md, Co-op): it finds the bot
/// copies on localhost, sends them what Mark types and the commands that belong to them, and
/// brings their lines into his chat log and onto their players as speech bubbles. A bot copy
/// never relays. The relay works on its own pool tasks; the only game work here is
/// <see cref="Tick"/>, on the main thread: it refreshes the list of bot copies the game started
/// and their nameplate names, and when this copy enters a co-op lobby it asks the bots to look
/// for it now (<see cref="CoopRelay.NudgeBots"/>) rather than at their next retry. A failure
/// only loses co-op, never the bridge, the chat or the brain. When the game
/// quits, the relay is stopped and its log goes quiet, since the engine it prints through may
/// already be gone.
/// </summary>
internal static class CoopWiring
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static CoopBotsAdapter? _adapter;
    private static CoopRelay? _relay;
    private static DateTime _nextTick;
    private static Func<bool>? _coopLobby;
    private static Action<string>? _warn;
    private static bool _wasInCoop;
    private static bool _nudgeFailed;
    private static int _shutDown;
    private static volatile bool _quiet;

    /// <summary>
    /// Builds and starts the relay for the human's copy, probing the ports in
    /// SPIREMIND_BOT_PORTS or else 17773 to 17775. Call once on the main thread, after the
    /// bridge and the chat box have started.
    /// </summary>
    /// <param name="inCoop">True in a co-op run or lobby; outside co-op only what names a linked
    /// bot goes to it. Called from the chat's threads, so it must return at once and not touch
    /// the game.</param>
    /// <param name="nameOf">The name this copy's nameplates give a player (CoopNames.NameOf),
    /// or null; read on the main thread only, by <see cref="Tick"/>.</param>
    /// <param name="coopLobby">The frame tick's own look at whether this copy is in a co-op lobby
    /// or run (InstancePoll.InCoop). Read on the main thread by <see cref="Tick"/>: when it turns
    /// true, the bots are asked to look for the lobby now.</param>
    /// <param name="notesRoot">Where the brain keeps its notes (BrainWiring.NotesRoot); the party
    /// plan goes to runs/party/plan.md under it. Null keeps the plan in memory only.</param>
    /// <param name="humanName">The human's name, for the planning sessions' chat lines and the plan.</param>
    /// <param name="humanCharacter">The human's character for the plan's heading (the chat's
    /// BotName, which the identity poll keeps on the local player). Called from the relay's
    /// tasks, so it must not touch the game.</param>
    /// <returns>The running relay; null for a bot copy, or when it couldn't start (logged).</returns>
    internal static CoopRelay? StartRelay(InstanceRole role, ChatHub chat, int port, MainThreadQueue main,
        Func<bool> inCoop, Func<bool> coopLobby, Func<ulong, string?>? nameOf, Action<string> log, Action<string> logError,
        string? notesRoot = null, string? humanName = null, Func<string?>? humanCharacter = null)
    {
        if (role.IsBot)
            return null;

        // The relay logs from its pool tasks; once the game is quitting, nothing is printed.
        // The wrappers call the loggers they were given, kept under their own names: a lambda
        // that read `log` after it was pointed at the wrapper would call itself until the
        // stack ran out, which crashed the game at load.
        var logTo = log;
        var logErrorTo = logError;
        Action<string> info = line => { if (!_quiet) logTo(line); };
        Action<string> warn = line => { if (!_quiet) logErrorTo(line); };
        log = info;
        logError = warn;

        try
        {
            var ports = CoopRelayOptions.ParsePorts(System.Environment.GetEnvironmentVariable(CoopRelayOptions.PortsEnvVar), out var warning)
                        ?? CoopRelayOptions.DefaultProbePorts;
            if (warning is not null)
                logError($"coop: {warning}");

            // Null when the game side can't start bot copies or draw their bubbles; the relay
            // still links bot copies started by hand (scripts/coop-bot.sh).
            var adapter = CoopBotsAdapter.TryCreate(main, logError, nameOf);
            var options = new CoopRelayOptions
            {
                OwnPort = port,
                ProbePorts = ports,
                PartyPlanFile = notesRoot is null ? null : System.IO.Path.Combine(notesRoot, "runs", "party", "plan.md"),
                HumanName = humanName,
                HumanCharacter = humanCharacter,
            };
            var relay = new CoopRelay(chat, options, adapter, line => log($"coop: {line}"), inCoop: inCoop);
            relay.Start();

            _adapter = adapter;
            _coopLobby = coopLobby;
            _warn = warn;
            _relay = relay;
            HookExit(relay, warn);
            return relay;
        }
        catch (Exception ex)
        {
            logError($"coop: the chat relay to bot copies couldn't start, so bots can't be talked to from this window: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Stops the relay when the game quits: when the scene tree's root leaves the tree and, in
    /// case that never comes, when the process exits. Only the first does anything; the log is
    /// muted first, so a poll failing during teardown prints nothing.
    /// </summary>
    private static void HookExit(CoopRelay relay, Action<string> warn)
    {
        void ShutDown()
        {
            _quiet = true;
            if (Interlocked.Exchange(ref _shutDown, 1) != 0)
                return;
            _adapter = null;
            _relay = null;
            try { relay.Dispose(); }
            catch (Exception) { /* quitting anyway */ }
        }

        try { AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutDown(); }
        catch (Exception ex) { warn($"coop: couldn't hook process exit: {ex.Message}"); }

        try
        {
            if (Engine.GetMainLoop() is SceneTree { Root: { } root })
                root.Connect(Node.SignalName.TreeExiting, Callable.From(() => ShutDown()));
            else
                warn("coop: no scene tree to hook game exit on; relying on process exit");
        }
        catch (Exception ex)
        {
            warn($"coop: couldn't hook game exit: {ex.Message}");
        }
    }

    /// <summary>Called every frame on the main thread, after the poll behind coopLobby: when this
    /// copy has just entered a co-op lobby (or run), the bots are asked to look for it now;
    /// and at most once a second the bot copies the game knows it started and their nameplate
    /// names are refreshed, which the relay reads without waiting.</summary>
    internal static void Tick()
    {
        if (_relay is { } relay && _coopLobby is { } coopLobby && !_nudgeFailed)
        {
            try
            {
                var inCoop = coopLobby();
                // NudgeBots only queues the requests; it never waits on a bot.
                if (inCoop && !_wasInCoop)
                    relay.NudgeBots();
                _wasInCoop = inCoop;
            }
            catch (Exception ex)
            {
                _nudgeFailed = true;
                _warn?.Invoke($"coop: asking the bots to look for the lobby failed and is off (they still find it on their own): {ex}");
            }
        }

        if (_adapter is not { } adapter)
            return;
        var now = DateTime.UtcNow;
        if (now < _nextTick)
            return;
        _nextTick = now + Interval;
        adapter.Refresh();
    }
}
