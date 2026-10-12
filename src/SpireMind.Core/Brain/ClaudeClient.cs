using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
// The mod compiles Core with global usings for Godot and the game, and Godot has its own HttpClient.
// These aliases keep the .NET ones there.
using HttpClient = System.Net.Http.HttpClient;

namespace SpireMind.Core.Brain;

/// <summary>
/// A minimal client for the Claude Messages API: <c>POST /v1/messages</c> with the key in
/// x-api-key and anthropic-version 2023-06-01. No SDK, so the mod stays one DLL. Requests are
/// not streamed, so a long reply comes back in one piece after minutes of silence: each try
/// waits longer the larger the request's max_tokens (<see cref="TimeoutFor"/>), and TCP
/// keep-alive keeps the quiet connection from being dropped along the way.
///
/// Passing failures are retried, at most <see cref="MaxAttempts"/> tries in total: 429 after
/// its retry-after (unless it is the Console spend limit, or a request in fast mode is asked to
/// wait longer than <see cref="MaxFastRetryAfter"/>), and 500, 502, 503, 504, 529 and
/// network errors (including a request timing out) with exponential backoff from 1, 2, 4 and
/// 8 seconds plus up to half again of jitter. Anything else, or the last failure, throws
/// <see cref="ClaudeApiException"/> with a <see cref="ClaudeErrorKind"/>. Cancelling the
/// caller's token throws <see cref="OperationCanceledException"/> at once, also mid-wait.
///
/// Thread-safe. The key is kept in one private field and redacted from every message the
/// client logs or throws.
/// </summary>
public sealed class ClaudeClient : IClaudeApi, IDisposable
{
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const string ApiVersion = "2023-06-01";
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly Uri _messagesUri;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string>? _log;
    private readonly Func<double> _random;

    /// <param name="apiKey">The API key. Must be printable ASCII without spaces or quotes;
    /// otherwise <see cref="ArgumentException"/> (whose message doesn't show it).</param>
    /// <param name="handler">HTTP handler for tests; the client doesn't dispose it. Defaults to
    /// a <see cref="SocketsHttpHandler"/> owned by the client.</param>
    /// <param name="baseUrl">Defaults to <see cref="DefaultBaseUrl"/>.</param>
    /// <param name="delay">How to wait between tries; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
    /// Tests pass one that records the wait and returns at once.</param>
    /// <param name="log">Receives one line per retry. Optional.</param>
    /// <param name="random">Jitter source in [0, 1); defaults to <see cref="Random.Shared"/>.</param>
    public ClaudeClient(string apiKey, HttpMessageHandler? handler = null, string? baseUrl = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Action<string>? log = null, Func<double>? random = null)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new ArgumentException("The API key is empty.", nameof(apiKey));
        if (!BrainSettings.LooksLikeKey(apiKey))
            throw new ArgumentException("The API key has characters a key can't have (spaces, quotes or non-ASCII).", nameof(apiKey));

        _apiKey = apiKey;
        _messagesUri = new Uri((baseUrl ?? DefaultBaseUrl).TrimEnd('/') + "/v1/messages");
        _delay = delay ?? Task.Delay;
        _log = log;
        _random = random ?? System.Random.Shared.NextDouble;
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(30),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectCallback = ConnectWithKeepAliveAsync,
            })
            : new HttpClient(handler, disposeHandler: false);
        // Each try has its own deadline (RequestTimeout), so the client-wide one is off.
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SpireMind", "1.0"));
    }

    /// <summary>The deadline for one try, response body included, unless the request's
    /// max_tokens calls for a longer one (<see cref="TimeoutFor"/>).</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The deadline for one try of <paramref name="request"/>: <see cref="RequestTimeout"/>, or
    /// longer when its max_tokens could take longer to write at a slow 128000 tokens an hour
    /// (an hour at the models' 128000 limit, 30 minutes at 64000). A try cut off by its deadline
    /// is retried and billed again, so the deadline must outlast the longest reply.
    /// </summary>
    public TimeSpan TimeoutFor(JsonObject request)
    {
        var maxTokens = request["max_tokens"] is JsonValue v && v.TryGetValue<int>(out var n) ? Math.Max(0, n) : 0;
        var writing = TimeSpan.FromHours(1) * (maxTokens / 128_000.0);
        return writing > RequestTimeout ? writing : RequestTimeout;
    }

    /// <summary>A 429 asking to wait longer than this isn't retried; the exception carries the wait.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The same for a request that asks for the API's fast mode (a top-level
    /// <c>"speed": "fast"</c>): fast mode has a rate limit of its own, and the caller would
    /// rather send the request again at standard speed than wait long for it (ApiBrain does), so
    /// only short waits are made here. With the five tries, a fast request waits two minutes at
    /// most before its caller hears of the 429.</summary>
    public TimeSpan MaxFastRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Sent as the anthropic-beta header when not empty (compaction, context editing).</summary>
    public IReadOnlyList<string> Betas { get; init; } = [];

    /// <summary>A request key that isn't sent in the body: an array of betas this request needs,
    /// added to the anthropic-beta header after <see cref="Betas"/>.</summary>
    public const string BetasField = "anthropic-beta";

    /// <summary>
    /// Sends one Messages API request (the JSON body: model, max_tokens, system, messages,
    /// tools...) and returns the response. The request object is only read, never changed.
    /// </summary>
    public async Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var betas = Betas.ToList();
        string body;
        if (request[BetasField] is JsonArray wanted)
        {
            foreach (var beta in wanted)
                if (beta is JsonValue v && v.TryGetValue<string>(out var name) && !betas.Contains(name))
                    betas.Add(name);
            var copy = (JsonObject)request.DeepClone();
            copy.Remove(BetasField);
            body = copy.ToJsonString();
        }
        else
        {
            body = request.ToJsonString();
        }
        var timeout = TimeoutFor(request);
        var fast = request["speed"] is JsonValue speed && speed.TryGetValue<string>(out var asks) && asks == ModelPrices.FastSpeed;
        var longestWait = fast && MaxFastRetryAfter < MaxRetryAfter ? MaxFastRetryAfter : MaxRetryAfter;

        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var (response, failure) = await AttemptAsync(body, betas, attempt, timeout, longestWait, ct);
            if (response is not null)
                return response;

            var f = failure!;
            var last = attempt >= MaxAttempts;
            if (!f.Retryable || last)
                throw new ClaudeApiException(f.Kind, f.Status, f.ErrorType, f.Message, f.RequestId,
                    attempt, f.RetryAfter, f.Inner);

            var wait = f.RetryAfter is { } asked ? (asked < MaxRetryAfter ? asked : MaxRetryAfter) : Jittered(attempt);
            _log?.Invoke(Redact(
                $"Claude API: try {attempt} of {MaxAttempts} failed ({Describe(f)}); retrying in {wait.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s."));
            await _delay(wait, ct);
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>One failed try, before it becomes a <see cref="ClaudeApiException"/>.</summary>
    private sealed record Failure(ClaudeErrorKind Kind, int? Status, string? ErrorType, string Message,
        string? RequestId, bool Retryable, TimeSpan? RetryAfter = null, Exception? Inner = null);

    private async Task<(ClaudeResponse? Response, Failure? Failure)> AttemptAsync(string body, IReadOnlyList<string> betas, int attempt, TimeSpan timeout, TimeSpan longestWait, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        using var message = new HttpRequestMessage(HttpMethod.Post, _messagesUri)
        {
            Content = new StringContent(body, Encoding.UTF8),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        // Without validation: a validation error would print the value, and the key was checked
        // in the constructor.
        message.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        if (betas.Count > 0)
            message.Headers.TryAddWithoutValidation("anthropic-beta", string.Join(",", betas));

        try
        {
            using var reply = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, deadline.Token);
            var text = await reply.Content.ReadAsStringAsync(deadline.Token);
            var requestId = Header(reply, "request-id");
            var status = (int)reply.StatusCode;

            if (reply.IsSuccessStatusCode)
            {
                if (TryParseObject(text) is { } json)
                    return (new ClaudeResponse(json, requestId ?? Str(json["request_id"]), attempt), null);
                return (null, new Failure(ClaudeErrorKind.Server, status, null,
                    "The response body wasn't a JSON object.", requestId, Retryable: true));
            }

            return (null, Classify(status, reply.ReasonPhrase, text, requestId, RetryAfterOf(reply), longestWait));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Our deadline, or a timeout inside the handler (connect timeout). Not the caller.
            return (null, new Failure(ClaudeErrorKind.Network, null, "timeout",
                $"No response within {timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s.",
                null, Retryable: true, Inner: Safe(ex)));
        }
        catch (HttpRequestException ex)
        {
            return (null, new Failure(ClaudeErrorKind.Network, null, null, Redact(ex.Message), null, Retryable: true, Inner: Safe(ex)));
        }
        catch (IOException ex)
        {
            return (null, new Failure(ClaudeErrorKind.Network, null, null, Redact(ex.Message), null, Retryable: true, Inner: Safe(ex)));
        }
    }

    // Connects as the handler would, with TCP keep-alive probes every 15 s after 60 s of silence,
    // so a router doesn't drop the connection while a long reply is being written. Where an
    // option can't be set, the connection goes ahead without it.
    //
    // The name is resolved here and each address gets its own socket. Connecting by name on a
    // socket that has had an option set fails on macOS as soon as the first address doesn't
    // answer ("Sockets on this platform are invalid for use after a failed connection attempt"),
    // and with it every request.
    private static async ValueTask<System.IO.Stream> ConnectWithKeepAliveAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await System.Net.Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new System.Net.Sockets.Socket(address.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
            try
            {
                try
                {
                    socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.KeepAlive, true);
                    socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Tcp, System.Net.Sockets.SocketOptionName.TcpKeepAliveTime, 60);
                    socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Tcp, System.Net.Sockets.SocketOptionName.TcpKeepAliveInterval, 15);
                }
                catch (Exception ex) when (ex is System.Net.Sockets.SocketException or PlatformNotSupportedException)
                {
                    // Keep-alive is a nicety; the request works without it.
                }
                await socket.ConnectAsync(new System.Net.IPEndPoint(address, context.DnsEndPoint.Port), ct).ConfigureAwait(false);
                return new System.Net.Sockets.NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException && !ct.IsCancellationRequested)
            {
                // This address doesn't answer (IPv6 with no route, say); the next one may.
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw last ?? new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
    }

    /// <summary>Maps an error response to a kind and decides whether it is worth another try: a
    /// 429 is when it asks to wait no longer than <paramref name="longestWait"/>.</summary>
    private Failure Classify(int status, string? reason, string body, string? requestId, TimeSpan? retryAfter, TimeSpan longestWait)
    {
        var json = TryParseObject(body);
        var error = json?["error"] as JsonObject;
        var type = Str(error?["type"]);
        var message = Str(error?["message"]);
        if (string.IsNullOrWhiteSpace(message))
            message = body.Length > 0 ? Truncate(body.Trim(), 300) : reason ?? "";
        message = Redact(message);
        var code = Str((error?["details"] as JsonObject)?["error_code"]);
        requestId ??= Str(json?["request_id"]);

        Failure Fail(ClaudeErrorKind kind, bool retryable = false) =>
            new(kind, status, type, message, requestId, retryable, retryAfter);

        if (status == 402 || type == "billing_error"
            || (status == 400 && message.Contains("credit balance", StringComparison.OrdinalIgnoreCase)))
            return Fail(ClaudeErrorKind.OutOfCredit);
        if ((status == 400 && message.Contains("usage limits", StringComparison.OrdinalIgnoreCase))
            || (status == 429 && code == "enforced_spend_limit_reached"))
            return Fail(ClaudeErrorKind.SpendLimit);

        return status switch
        {
            401 or 403 => Fail(ClaudeErrorKind.Unauthorized),
            400 or 404 or 413 => Fail(ClaudeErrorKind.InvalidRequest),
            429 => Fail(ClaudeErrorKind.RateLimited, retryable: retryAfter is null || retryAfter <= longestWait),
            529 => Fail(ClaudeErrorKind.Overloaded, retryable: true),
            500 or 502 or 503 or 504 => Fail(ClaudeErrorKind.Server, retryable: true),
            >= 500 => Fail(ClaudeErrorKind.Server),
            _ => Fail(ClaudeErrorKind.Other),
        };
    }

    /// <summary>The base wait for the try after <paramref name="attempt"/> plus up to half again.</summary>
    private TimeSpan Jittered(int attempt)
    {
        var baseWait = Backoff[Math.Min(attempt, Backoff.Length) - 1];
        return baseWait + baseWait * (0.5 * Math.Clamp(_random(), 0, 1));
    }

    /// <summary>retry-after in seconds (possibly fractional) or as an HTTP date. Null when absent or unreadable.</summary>
    private static TimeSpan? RetryAfterOf(HttpResponseMessage reply)
    {
        var raw = Header(reply, "retry-after");
        if (raw is null)
            return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0 && seconds < 86400)
            return TimeSpan.FromSeconds(seconds);
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
        {
            var wait = when - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    private static string? Header(HttpResponseMessage reply, string name) =>
        reply.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;

    private static JsonObject? TryParseObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Describe(Failure f)
    {
        var what = f.Status is { } s ? $"HTTP {s.ToString(CultureInfo.InvariantCulture)}" : "no response";
        if (!string.IsNullOrEmpty(f.ErrorType)) what += " " + f.ErrorType;
        if (!string.IsNullOrEmpty(f.Message)) what += ": " + Truncate(f.Message, 200);
        if (!string.IsNullOrEmpty(f.RequestId)) what += $" [request {f.RequestId}]";
        return what;
    }

    private string Redact(string text) => text.Replace(_apiKey, "[redacted]", StringComparison.Ordinal);

    // The inner exception to keep, or null when its text would show the key: an exception's
    // ToString includes the inner one, and that text can't be redacted.
    private Exception? Safe(Exception ex) => ex.ToString().Contains(_apiKey, StringComparison.Ordinal) ? null : ex;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "...";

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
