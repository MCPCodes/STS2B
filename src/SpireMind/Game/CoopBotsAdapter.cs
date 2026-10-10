// The adapter to the game side's own bot launcher and bubbles (the static class
// SpireMind.Game.Coop.CoopBots, written on the Mac with the decompiled game). Every reference to
// that class lives in this one file. Until those Coop files land, the build leaves
// SPIREMIND_COOP_BOTS undefined and the #else branch below compiles instead: the relay then
// finds bot copies by probing ports, /bot add answers that this build can't start bot copies,
// and no bubbles are drawn. The Mac removes the #if (keeping the first branch) once its Coop
// files land.
#if SPIREMIND_COOP_BOTS
using System.Runtime.CompilerServices;
// The game side's own launcher and bubbles (src/SpireMind/Game/Coop/, written on the Mac). An
// alias, so every use below is plainly that static class.
using GameCoopBots = global::SpireMind.Game.Coop.CoopBots;
using ICoopBots = SpireMind.Core.Coop.ICoopBots;

namespace SpireMind.Game;

/// <summary>
/// The co-op relay's way into the game side's <see cref="GameCoopBots"/>: starting bot copies,
/// listing the ones started, and drawing a bot copy's line as a speech bubble over its player
/// on the human's screen. The relay calls it from its pool tasks; every call into the game
/// side is marshalled to the main thread and none of them blocks the caller.
/// <see cref="Known"/> is a snapshot that <see cref="Refresh"/> updates on the frame tick.
/// </summary>
internal sealed class CoopBotsAdapter : ICoopBots
{
    private static readonly (int Port, ulong NetId, string Character)[] None = [];

    private readonly MainThreadQueue _main;
    private readonly Action<string> _logError;
    private volatile (int Port, ulong NetId, string Character)[] _known = None;
    private int _refreshFailed;
    private int _showFailed;

    private CoopBotsAdapter(MainThreadQueue main, Action<string> logError)
    {
        _main = main;
        _logError = logError;
    }

    /// <summary>
    /// The adapter, or null when the game side's <see cref="GameCoopBots"/> can't be used
    /// (logged). <see cref="Known"/> starts empty and fills on the first <see cref="Refresh"/>,
    /// once the game is up. Call on the main thread.
    /// </summary>
    public static CoopBotsAdapter? TryCreate(MainThreadQueue main, Action<string> logError)
    {
        try
        {
            Prepare();
            return new CoopBotsAdapter(main, logError);
        }
        catch (Exception ex)
        {
            logError($"coop: the game side can't start bot copies or draw their bubbles, so /bot add won't work: {ex}");
            return null;
        }
    }

    /// <summary>The bot copies the game side started: bridge port, NetId and character. A
    /// snapshot from the last <see cref="Refresh"/>; safe from any thread.</summary>
    public IReadOnlyList<(int Port, ulong NetId, string Character)> Known => _known;

    /// <summary>Main thread, from the frame tick: takes a fresh snapshot of
    /// <see cref="Known"/>. A failure keeps the last one (logged once).</summary>
    public void Refresh()
    {
        try
        {
            _known = ReadKnown();
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _refreshFailed, 1) == 0)
                _logError($"coop: couldn't list the bot copies the game started: {ex}");
        }
    }

    /// <summary>Starts a bot copy on the main thread and returns the game side's line for the
    /// chat. Returns at once; the task ends when the game side's start does.</summary>
    public Task<string> StartCopyAsync(string? character, CancellationToken ct) =>
        _main.Run(() => StartCopy(character, ct)).Unwrap();

    /// <summary>Queues a speech bubble over the player with this NetId for the next frame.
    /// Never waits; a failure is logged once.</summary>
    public void ShowLine(ulong netId, string text)
    {
        _ = _main.Run(() => ShowLineNow(netId, text)).ContinueWith(
            t =>
            {
                if (Interlocked.Exchange(ref _showFailed, 1) == 0)
                    _logError($"coop: couldn't draw a bot copy's speech bubble: {t.Exception?.GetBaseException()}");
            },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // The only calls into the game side's class, each in its own method, so a member that
    // doesn't match fails inside the caller's try and not while the caller is compiled.

    // Runs the class's static initialization now, so a class that can't initialize is found at
    // load and not on every call.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Prepare() => RuntimeHelpers.RunClassConstructor(typeof(GameCoopBots).TypeHandle);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Port, ulong NetId, string Character)[] ReadKnown() =>
        GameCoopBots.Known is { Count: > 0 } known ? known.ToArray() : None;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<string> StartCopy(string? character, CancellationToken ct) =>
        GameCoopBots.StartCopyAsync(character, ct);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowLineNow(ulong netId, string text) => GameCoopBots.ShowLine(netId, text);
}

#else
using ICoopBots = SpireMind.Core.Coop.ICoopBots;

namespace SpireMind.Game;

/// <summary>
/// Stand-in until the game side's bot launcher lands (see the header): there is no adapter, so
/// the relay probes ports for bot copies, answers /bot add with a line saying this build can't
/// start them, and draws no bubbles.
/// </summary>
internal sealed class CoopBotsAdapter : ICoopBots
{
    private CoopBotsAdapter() { }

    /// <summary>Always null in this build, logged once so the missing piece is plain.</summary>
    public static CoopBotsAdapter? TryCreate(MainThreadQueue main, Action<string> logError)
    {
        _ = main;
        logError("coop: this build has no bot launcher (src/SpireMind/Game/Coop/ hasn't landed), so /bot add can't start copies and bot lines show no bubbles; bot copies started by hand are still found on their ports.");
        return null;
    }

    public IReadOnlyList<(int Port, ulong NetId, string Character)> Known => [];

    public void Refresh() { }

    public Task<string> StartCopyAsync(string? character, CancellationToken ct) =>
        Task.FromResult("This build can't start bot copies yet; start one with scripts/coop-bot.sh.");

    public void ShowLine(ulong netId, string text) { }
}
#endif
