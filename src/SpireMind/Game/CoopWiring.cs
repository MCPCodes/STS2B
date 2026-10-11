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
/// <see cref="Tick"/>, which refreshes the list of bot copies the game started, on the main
/// thread. A failure only loses co-op, never the bridge, the chat or the brain. When the game
/// quits, the relay is stopped and its log goes quiet, since the engine it prints through may
/// already be gone.
/// </summary>
internal static class CoopWiring
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static CoopBotsAdapter? _adapter;
    private static DateTime _nextTick;
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
    /// <returns>The running relay; null for a bot copy, or when it couldn't start (logged).</returns>
    internal static CoopRelay? StartRelay(InstanceRole role, ChatHub chat, int port, MainThreadQueue main,
        Func<bool> inCoop, Action<string> log, Action<string> logError)
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
            var adapter = CoopBotsAdapter.TryCreate(main, logError);
            var relay = new CoopRelay(chat, new CoopRelayOptions { OwnPort = port, ProbePorts = ports }, adapter,
                line => log($"coop: {line}"), inCoop: inCoop);
            relay.Start();

            _adapter = adapter;
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

    /// <summary>Called every frame on the main thread; at most once a second it refreshes the
    /// bot copies the game knows it started, which the relay reads without waiting.</summary>
    internal static void Tick()
    {
        if (_adapter is not { } adapter)
            return;
        var now = DateTime.UtcNow;
        if (now < _nextTick)
            return;
        _nextTick = now + Interval;
        adapter.Refresh();
    }
}
