using System.Globalization;

namespace SpireMind.Core.Brain;

/// <summary>Why a Messages API call failed, coarse enough for the brain to decide what to do.</summary>
public enum ClaudeErrorKind
{
    /// <summary>No credit left: 402, a billing_error, or a 400 whose message mentions the credit balance.</summary>
    OutOfCredit,
    /// <summary>The spend limit set in the Claude Console: a 400 about usage limits, or a 429 with
    /// error_code enforced_spend_limit_reached.</summary>
    SpendLimit,
    /// <summary>The key was rejected: 401 or 403.</summary>
    Unauthorized,
    /// <summary>The request itself is wrong: other 400s, 404, 413. Retrying won't help.</summary>
    InvalidRequest,
    /// <summary>429 rate limit, still failing after the retries (or asking for a very long wait).</summary>
    RateLimited,
    /// <summary>529, still failing after the retries.</summary>
    Overloaded,
    /// <summary>No usable response: connection failed, timed out or was cut off.</summary>
    Network,
    /// <summary>5xx, still failing after the retries where they apply.</summary>
    Server,
    /// <summary>Anything else.</summary>
    Other,
}

/// <summary>
/// A Messages API call that failed for good: not retryable, or still failing after
/// <see cref="ClaudeClient.MaxAttempts"/> tries. The message says what happened in one line
/// with the HTTP status, error type, the API's own message and the request id. It never
/// contains the API key.
/// </summary>
public sealed class ClaudeApiException : Exception
{
    public ClaudeApiException(ClaudeErrorKind kind, int? status, string? errorType, string apiMessage,
        string? requestId, int attempts = 1, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(Compose(kind, status, errorType, apiMessage, requestId, attempts), inner)
    {
        Kind = kind;
        Status = status;
        ErrorType = errorType;
        ApiMessage = apiMessage;
        RequestId = requestId;
        Attempts = attempts;
        RetryAfter = retryAfter;
    }

    public ClaudeErrorKind Kind { get; }

    /// <summary>The HTTP status, or null when no response came back.</summary>
    public int? Status { get; }

    /// <summary>The API's error.type (rate_limit_error, overloaded_error, billing_error...), or null.</summary>
    public string? ErrorType { get; }

    /// <summary>The API's error.message, or a description of the network failure.</summary>
    public string ApiMessage { get; }

    public string? RequestId { get; }

    /// <summary>How many tries were made before giving up.</summary>
    public int Attempts { get; }

    /// <summary>The retry-after the server asked for on the last try, if any.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// True when the API can't be used until the human does something (a working key, more
    /// credit, a higher Console spend limit), so the brain should hand over to the subscription
    /// fallback rather than retry later. Rate limits, overload, server and network errors are
    /// passing and return false.
    /// </summary>
    public bool NeedsHuman => Kind is ClaudeErrorKind.OutOfCredit or ClaudeErrorKind.SpendLimit or ClaudeErrorKind.Unauthorized;

    /// <summary>True for a 400 saying the prompt is over the context window: start a fresh conversation.</summary>
    public bool PromptTooLong =>
        Kind == ClaudeErrorKind.InvalidRequest && ApiMessage.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase);

    /// <summary>A short sentence for the in-game chat.</summary>
    public string Summary => Describe(Kind);

    private static string Describe(ClaudeErrorKind kind) => kind switch
    {
        ClaudeErrorKind.OutOfCredit => "The API credit has run out.",
        ClaudeErrorKind.SpendLimit => "The API key reached the spend limit set in the Claude Console.",
        ClaudeErrorKind.Unauthorized => "The API key was rejected.",
        ClaudeErrorKind.InvalidRequest => "The API refused the request as invalid.",
        ClaudeErrorKind.RateLimited => "The API is rate limiting us.",
        ClaudeErrorKind.Overloaded => "The API is overloaded.",
        ClaudeErrorKind.Network => "Couldn't reach the API.",
        ClaudeErrorKind.Server => "The API had a server error.",
        _ => "The API call failed.",
    };

    private static string Compose(ClaudeErrorKind kind, int? status, string? errorType, string apiMessage, string? requestId, int attempts)
    {
        var detail = status is { } s ? $"HTTP {s.ToString(CultureInfo.InvariantCulture)}" : "no response";
        if (!string.IsNullOrEmpty(errorType)) detail += " " + errorType;
        if (!string.IsNullOrEmpty(apiMessage)) detail += ": " + apiMessage;
        if (attempts > 1) detail += $" (after {attempts.ToString(CultureInfo.InvariantCulture)} tries)";
        if (!string.IsNullOrEmpty(requestId)) detail += $" [request {requestId}]";
        return $"{Describe(kind)} {detail}";
    }
}
