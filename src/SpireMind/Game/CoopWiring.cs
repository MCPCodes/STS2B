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
/// thread. A failure only loses co-op, never the bridge, the chat or the brain.
/// </summary>
internal static class CoopWiring
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static CoopBotsAdapter? _adapter;
    private static DateTime _nextTick;

    /// <summary>
    /// Builds and starts the relay for the human's copy, probing the ports in
    /// SPIREMIND_BOT_PORTS or else 17773 to 17775. Call once on the main thread, after the
    /// bridge has started.
    /// </summary>
    /// <returns>The running relay; null for a bot copy, or when it couldn't start (logged).</returns>
    internal static CoopRelay? StartRelay(InstanceRole role, ChatHub chat, int port, MainThreadQueue main,
        Action<string> log, Action<string> logError)
    {
        if (role.IsBot)
            return null;

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
                line => log($"coop: {line}"));
            relay.Start();

            _adapter = adapter;
            return relay;
        }
        catch (Exception ex)
        {
            logError($"coop: the chat relay to bot copies couldn't start, so bots can't be talked to from this window: {ex}");
            return null;
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
