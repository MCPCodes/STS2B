// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using InstanceInfo = SpireMind.Core.Coop.InstanceInfo;

namespace SpireMind.Game;

/// <summary>
/// Keeps <see cref="InstanceInfo.NetId"/> on the local player's NetId, which the bridge reports
/// on /coop/info and /chat, so the human's copy can tell which player a bot copy's line belongs
/// to and draw its bubble there. The NetId is the lobby's at character select and the run's in
/// a run (docs/game-api.md, sections a and g), and null anywhere else. The same read tells
/// whether this copy is in co-op (<see cref="InCoop"/>), lobby included, for the co-op relay and
/// the human copy's brain gate.
/// </summary>
internal static class InstancePoll
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static DateTime _nextRead;
    private static ulong? _last;
    private static volatile bool _inCoop;

    /// <summary>True when the last read (at most a second old) found a co-op lobby or run: a
    /// net service of type Host or Client. Safe from any thread.</summary>
    public static bool InCoop => _inCoop;

    /// <summary>Called every frame on the main thread; reads the game at most once a second.</summary>
    public static void Poll(InstanceInfo info)
    {
        var now = DateTime.UtcNow;
        if (now < _nextRead) return;
        _nextRead = now + Interval;

        var (netId, inCoop) = Read();
        info.NetId = netId;
        _inCoop = inCoop;
        if (netId == _last) return;
        _last = netId;
        if (info.IsBot)
            ModEntry.Log(netId is { } id ? $"coop: this copy's player has NetId {id}" : "coop: this copy has no player right now");
    }

    /// <summary>
    /// The local player's NetId, or null outside the lobby and a run. Character select comes
    /// first: the screen is created with the main menu, so it counts only on top of the submenu
    /// stack with a lobby, and a run being set up there already counts as in progress. In a run,
    /// LocalContext's NetId is set at launch; the run's net service has it before that. Any
    /// failure (a run loading or tearing down) reads as null. Co-op is a net service of type
    /// Host or Client there, singleplayer and replays aren't.
    /// </summary>
    private static (ulong? NetId, bool InCoop) Read()
    {
        try
        {
            if (NGame.Instance?.MainMenu?.SubmenuStack?.Peek() is NCharacterSelectScreen { Lobby: { } lobby })
                return (lobby.NetService?.NetId, IsCoop(lobby.NetService?.Type));

            var rm = RunManager.Instance;
            if (rm is { IsInProgress: true, NetService: { } net })
                return (LocalContext.NetId ?? net.NetId, IsCoop(net.Type));
            return (null, false);
        }
        catch
        {
            return (null, false);
        }
    }

    private static bool IsCoop(NetGameType? type) => type is NetGameType.Host or NetGameType.Client;
}
