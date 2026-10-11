using System.Runtime.CompilerServices;
// Aliases rather than the namespaces, so a game type of the same name in the global usings can
// never make them ambiguous. The Coop classes are the game side of co-op (src/SpireMind/Game/Coop/,
// written on the Mac).
using BotStatus = SpireMind.Core.Coop.BotStatus;
using GameCoopBots = global::SpireMind.Game.Coop.CoopBots;
using GameJoinWatcher = global::SpireMind.Game.Coop.JoinWatcher;
using GameLobbyDriver = global::SpireMind.Game.Coop.LobbyDriver;
using InstanceInfo = SpireMind.Core.Coop.InstanceInfo;

namespace SpireMind.Game;

/// <summary>
/// A bot copy's side of what the co-op relay in the human's copy asks of it through the bridge:
/// where it stands in the lobby (for /bots, on /coop/info and /chat), leaving and quitting (POST
/// /coop/quit, for /bot stop) and looking for the host's lobby now (POST /coop/join-now, when the
/// human's copy has just opened one). The bridge calls these on HTTP worker threads, so none of
/// them touches the game there: the status is a snapshot <see cref="Tick"/> takes on the main
/// thread twice a second, and the two requests are queued for the main thread and answered at
/// once. Only a bot copy is wired; the human's copy never is.
/// </summary>
internal static class BotSelf
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    private static MainThreadQueue? _main;
    private static volatile BotStatus? _status;
    // 1 when the game side runs this copy as a bot copy (it can leave and quit by itself), 0
    // when it doesn't, -1 before the first snapshot.
    private static volatile int _quits = -1;
    private static DateTime _nextRead;
    private static bool _readFailed;

    /// <summary>
    /// Gives this bot copy's <paramref name="info"/> its status and its two requests. Call once
    /// on the main thread at mod load, before the bridge starts. Does nothing for the human's
    /// copy. Never throws.
    /// </summary>
    internal static void Wire(InstanceInfo info, MainThreadQueue main)
    {
        try
        {
            if (!info.IsBot)
                return;
            _main = main;
            info.Status = ReadSnapshot;
            info.Quit = Quit;
            info.RetryJoin = RetryJoin;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: this bot copy can't tell the relay where it stands or take /bot stop: {ex}");
        }
    }

    /// <summary>Every frame on the main thread: at most twice a second, a fresh snapshot of where
    /// this bot copy stands. A failure is logged once and leaves the status unreported.</summary>
    internal static void Tick()
    {
        if (_main is null || _readFailed)
            return;
        var now = DateTime.UtcNow;
        if (now < _nextRead)
            return;
        _nextRead = now + Interval;
        try
        {
            _quits = IsBotCopy() ? 1 : 0;
            _status = ReadStatus();
        }
        catch (Exception ex)
        {
            _readFailed = true;
            _status = null;
            ModEntry.LogError($"coop: reading where this bot copy stands failed, so /bots won't show it: {ex}");
        }
    }

    // HTTP worker thread: the last snapshot, or null before the first.
    private static BotStatus? ReadSnapshot() => _status;

    // HTTP worker thread, for POST /coop/quit: the game's own leave and quit, on the main thread.
    private static string Quit(string from)
    {
        if (_quits == 0)
            return "This copy can't leave and quit by itself (its log says why); close its window.";
        var why = $"{from} asked with /bot stop";
        OnMain(() => RequestQuit(why), "leaving and quitting for /bot stop");
        return "Leaving the game and quitting.";
    }

    // HTTP worker thread, for POST /coop/join-now.
    private static void RetryJoin()
    {
        ModEntry.Log("coop: the host's copy opened a lobby; looking for it now");
        OnMain(RetryNow, "looking for the lobby now");
    }

    // Queues the work for the next frame and returns at once; a failure there is logged.
    private static void OnMain(Action work, string what)
    {
        if (_main is not { } main)
            return;
        _ = main.Run(work).ContinueWith(
            t => ModEntry.LogError($"coop: {what} failed: {t.Exception?.GetBaseException()}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // The only calls into the game side's classes, each in its own method, so a member that
    // doesn't match fails inside the caller's try and not while the caller is compiled.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BotStatus ReadStatus() =>
        new(GameLobbyDriver.Status, GameLobbyDriver.Character, GameLobbyDriver.InRun, GameJoinWatcher.Stopped);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsBotCopy() => GameCoopBots.IsBotCopy;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RequestQuit(string why) => GameCoopBots.RequestQuit(why);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RetryNow() => GameJoinWatcher.RetryNow();
}
