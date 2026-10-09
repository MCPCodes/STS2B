using System.Collections.Concurrent;

namespace SpireMind.Core;

/// <summary>
/// Godot objects may only be touched from the main thread. HTTP handlers enqueue work
/// here and the mod drains it once per frame from the SceneTree's process_frame signal.
/// </summary>
public sealed class MainThreadQueue
{
    private readonly ConcurrentQueue<Action> _work = new();

    /// <summary>Upper bound per frame so a burst of requests can't stall rendering.</summary>
    public int MaxPerFrame { get; init; } = 16;

    public Action<Exception>? OnError { get; init; }

    public Task<T> Run<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Enqueue(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public Task Run(Action action) => Run(() => { action(); return true; });

    /// <summary>Called by the game once per frame on the main thread.</summary>
    public void Drain()
    {
        for (var i = 0; i < MaxPerFrame && _work.TryDequeue(out var work); i++)
        {
            try { work(); }
            catch (Exception ex) { OnError?.Invoke(ex); }
        }
    }
}
