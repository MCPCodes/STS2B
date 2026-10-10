// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using InstanceInfo = SpireMind.Core.Coop.InstanceInfo;

namespace SpireMind.Game;

/// <summary>
/// Keeps <see cref="InstanceInfo.NetId"/> on the local player's NetId, which the bridge reports
/// on /coop/info and /chat, so the human's copy can tell which player a bot copy's line belongs
/// to and draw its bubble there. The NetId is the lobby's at character select and the run's in
/// a run (docs/game-api.md, sections a and g), and null anywhere else.
/// </summary>
internal static class InstancePoll
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static DateTime _nextRead;
    private static ulong? _last;

    /// <summary>Called every frame on the main thread; reads the game at most once a second.</summary>
    public static void Poll(InstanceInfo info)
    {
        var now = DateTime.UtcNow;
        if (now < _nextRead) return;
        _nextRead = now + Interval;

        var netId = ReadNetId();
        info.NetId = netId;
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
    /// failure (a run loading or tearing down) reads as null.
    /// </summary>
    private static ulong? ReadNetId()
    {
        try
        {
            if (NGame.Instance?.MainMenu?.SubmenuStack?.Peek() is NCharacterSelectScreen { Lobby: { } lobby })
                return lobby.NetService?.NetId;

            var rm = RunManager.Instance;
            if (rm is { IsInProgress: true, NetService: { } net })
                return LocalContext.NetId ?? net.NetId;
            return null;
        }
        catch
        {
            return null;
        }
    }
}
