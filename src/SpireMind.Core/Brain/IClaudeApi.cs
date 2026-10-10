using System.Text.Json.Nodes;

namespace SpireMind.Core.Brain;

/// <summary>
/// One call to the Messages API. <see cref="ClaudeClient"/> is the real one; tests script
/// responses. Failures throw <see cref="ClaudeApiException"/> with a <see cref="ClaudeErrorKind"/>
/// the brain can act on, after the implementation's own retries for 429, 529 and 5xx.
/// </summary>
public interface IClaudeApi
{
    /// <summary>Send <paramref name="request"/> (a Messages API body) and return the parsed
    /// response. Must not keep or change the request object.</summary>
    Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct);
}
