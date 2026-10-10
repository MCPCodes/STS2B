using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.DevHost;

/// <summary>
/// Passes at most <see cref="Max"/> requests on to the real Messages API and refuses the rest,
/// so a smoke test can't run on. A refused request throws a <see cref="ClaudeApiException"/>
/// of kind Other, which <see cref="ApiBrain"/> takes as a request it can't send and stops with
/// <see cref="BrainStopReason.Error"/>; <see cref="CapReached"/> tells that stop apart from a
/// real one. Each response that comes back, and each failure, is reported first. A cancelled
/// call throws before it counts. The brain sends one request at a time, but the counts are
/// safe from any thread.
/// </summary>
public sealed class RequestCap : IClaudeApi
{
    private readonly IClaudeApi _inner;
    private readonly Action<JsonObject, ClaudeResponse>? _onResponse;
    private readonly Action<Exception>? _onFailure;
    private int _sent;
    private int _failed;
    private int _refused;

    /// <param name="inner">The real API (<see cref="ClaudeClient"/>).</param>
    /// <param name="max">How many requests may go through, at least 1.</param>
    /// <param name="onResponse">Gets each request with its response, on the caller's thread.</param>
    /// <param name="onFailure">Gets each exception the real API threw (cancellation aside), before it goes on to the caller.</param>
    public RequestCap(IClaudeApi inner, int max, Action<JsonObject, ClaudeResponse>? onResponse = null, Action<Exception>? onFailure = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        _inner = inner;
        Max = max;
        _onResponse = onResponse;
        _onFailure = onFailure;
    }

    public int Max { get; }

    /// <summary>Requests passed on to the real API, failed ones included.</summary>
    public int Sent => Volatile.Read(ref _sent);

    /// <summary>Requests the real API failed (an error after its own retries, or a network failure).</summary>
    public int Failed => Volatile.Read(ref _failed);

    /// <summary>True once a request was refused for being over the cap.</summary>
    public bool CapReached => Volatile.Read(ref _refused) != 0;

    public async Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _sent) > Max)
        {
            Interlocked.Decrement(ref _sent);
            Volatile.Write(ref _refused, 1);
            throw new ClaudeApiException(ClaudeErrorKind.Other, null, "request_cap",
                $"The smoke test's cap of {Max} requests was reached.", null);
        }

        ClaudeResponse response;
        try
        {
            response = await _inner.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failed);
            _onFailure?.Invoke(ex);
            throw;
        }
        _onResponse?.Invoke(request, response);
        return response;
    }
}
