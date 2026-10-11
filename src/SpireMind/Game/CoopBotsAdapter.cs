// The adapter to the game side's own bot launcher and bubbles (the static classes
// SpireMind.Game.Coop.CoopBots and RemoteBubbles, written on the Mac with the decompiled game).
// Every reference to them from the relay's side lives in this file; the nameplate names
// (SpireMind.Patches.CoopNames) come in as a function from ModEntry. Those Coop files have
// landed, so the stand-in that compiled without them (and its SPIREMIND_COOP_BOTS switch) is gone.
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
// The game side's own launcher and bubbles (src/SpireMind/Game/Coop/, written on the Mac). A
// namespace alias, so every use below is plainly one of its static classes.
using GameCoop = global::SpireMind.Game.Coop;
using ICoopBots = SpireMind.Core.Coop.ICoopBots;

namespace SpireMind.Game;

/// <summary>
/// The co-op relay's way into the game side's <see cref="GameCoop.CoopBots"/>: starting and stopping
/// bot copies, listing the ones started, drawing a bot copy's line as a speech bubble and its
/// thought cloud over its player on the human's screen, and the names that screen's nameplates
/// give the bots. The relay calls it from its pool tasks; every call into the game side is
/// marshalled to the main thread and none of them blocks the caller. <see cref="Known"/> and
/// <see cref="NameOf"/> are snapshots that <see cref="Refresh"/> updates on the frame tick.
/// </summary>
internal sealed class CoopBotsAdapter : ICoopBots
{
    private static readonly (int Port, ulong NetId, string Character)[] None = [];

    /// <summary>More NetIds than a lobby holds players means something asks for stray ones: start over.</summary>
    private const int MaxNames = 16;

    private readonly MainThreadQueue _main;
    private readonly Action<string> _logError;
    private readonly Func<ulong, string?>? _readName;
    private volatile (int Port, ulong NetId, string Character)[] _known = None;
    private int _refreshFailed;
    private int _namesFailed;
    private int _showFailed;
    private int _cloudFailed;
    // The nameplate names of the NetIds the relay has asked about, read on the main thread by
    // Refresh; null where the screen shows none.
    private readonly ConcurrentDictionary<ulong, string?> _names = new();

    private CoopBotsAdapter(MainThreadQueue main, Action<string> logError, Func<ulong, string?>? readName)
    {
        _main = main;
        _logError = logError;
        _readName = readName;
    }

    /// <summary>
    /// The adapter, or null when the game side's <see cref="GameCoop.CoopBots"/> can't be used
    /// (logged). <see cref="Known"/> starts empty and fills on the first <see cref="Refresh"/>,
    /// once the game is up. Call on the main thread.
    /// </summary>
    /// <param name="readName">The name the nameplates give a player of this copy's local co-op
    /// game, or null. Called by <see cref="Refresh"/> on the main thread only. Null leaves the
    /// relay calling the bots by character.</param>
    public static CoopBotsAdapter? TryCreate(MainThreadQueue main, Action<string> logError, Func<ulong, string?>? readName = null)
    {
        try
        {
            Prepare();
            return new CoopBotsAdapter(main, logError, readName);
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

        if (_readName is not { } readName)
            return;
        try
        {
            if (_names.Count > MaxNames)
                _names.Clear();
            foreach (var netId in _names.Keys)
                _names[netId] = readName(netId);
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _namesFailed, 1) == 0)
                _logError($"coop: couldn't read the bots' nameplate names, so the chat calls them by character: {ex}");
        }
    }

    /// <summary>The nameplate name of the player with this NetId as of the last
    /// <see cref="Refresh"/> (at most a second old), or null. A NetId asked about for the first
    /// time reads as null until then. Never waits; safe from any thread.</summary>
    public string? NameOf(ulong netId) => _names.GetOrAdd(netId, (string?)null);

    /// <summary>Asks every bot copy the game started to leave and quit, on the main thread.
    /// Returns at once; the task ends with the game side's line once they have.</summary>
    public Task<string> StopAllAsync(CancellationToken ct) =>
        _main.Run(() => StopAll(ct)).Unwrap();

    /// <summary>Queues a thought cloud on or off over the player with this NetId for the next
    /// frame. Never waits; a failure is logged once.</summary>
    public void Thinking(ulong netId, bool on)
    {
        _ = _main.Run(() => ThinkingNow(netId, on)).ContinueWith(
            t =>
            {
                if (Interlocked.Exchange(ref _cloudFailed, 1) == 0)
                    _logError($"coop: couldn't draw a bot copy's thought cloud: {t.Exception?.GetBaseException()}");
            },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
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
    private static void Prepare() => RuntimeHelpers.RunClassConstructor(typeof(GameCoop.CoopBots).TypeHandle);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Port, ulong NetId, string Character)[] ReadKnown() =>
        GameCoop.CoopBots.Known is { Count: > 0 } known ? known.ToArray() : None;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<string> StartCopy(string? character, CancellationToken ct) =>
        GameCoop.CoopBots.StartCopyAsync(character, ct);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ShowLineNow(ulong netId, string text) => GameCoop.CoopBots.ShowLine(netId, text);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<string> StopAll(CancellationToken ct) => GameCoop.CoopBots.StopAllAsync(ct);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThinkingNow(ulong netId, bool on) => GameCoop.RemoteBubbles.Thinking(netId, on);
}
