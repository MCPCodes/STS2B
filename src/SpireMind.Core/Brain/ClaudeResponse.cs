using System.Text.Json.Nodes;

namespace SpireMind.Core.Brain;

/// <summary>One tool call the model made: a <c>tool_use</c> block. Input is a detached copy, safe to keep or pass on.</summary>
public sealed record ToolUseBlock(string Id, string Name, JsonObject Input);

/// <summary>
/// A successful Messages API response: the raw JSON exactly as received plus typed helpers.
/// Not thread-safe (JsonNode isn't); the brain uses each response on its own task.
/// </summary>
public sealed class ClaudeResponse
{
    public ClaudeResponse(JsonObject raw, string? requestId = null, int attempts = 1)
    {
        Raw = raw;
        RequestId = requestId ?? Str(raw["request_id"]);
        Attempts = attempts;
    }

    /// <summary>The response body as received.</summary>
    public JsonObject Raw { get; }

    /// <summary>The request-id header, for reports to Anthropic and the transcript.</summary>
    public string? RequestId { get; }

    /// <summary>How many tries it took (1 when the first one worked).</summary>
    public int Attempts { get; }

    public string? Id => Str(Raw["id"]);

    /// <summary>The model that answered, which is the one billed. Price with this when present.</summary>
    public string? Model => Str(Raw["model"]);

    /// <summary>end_turn, tool_use, max_tokens, pause_turn, refusal, model_context_window_exceeded...</summary>
    public string? StopReason => Str(Raw["stop_reason"]);

    /// <summary>The content blocks (thinking, text, tool_use...) as received. These nodes belong to
    /// <see cref="Raw"/>; use <see cref="ToAssistantMessage"/> to send them back.</summary>
    public JsonArray Content => Raw["content"] as JsonArray ?? new JsonArray();

    public JsonObject? UsageJson => Raw["usage"] as JsonObject;

    public TokenUsage Usage => TokenUsage.FromJson(UsageJson);

    /// <summary>The tool_use blocks in order.</summary>
    public IReadOnlyList<ToolUseBlock> ToolUses =>
        Content.OfType<JsonObject>()
            .Where(b => Str(b["type"]) == "tool_use")
            .Select(b => new ToolUseBlock(
                Str(b["id"]) ?? "",
                Str(b["name"]) ?? "",
                b["input"] is JsonObject input ? (JsonObject)input.DeepClone() : new JsonObject()))
            .ToArray();

    /// <summary>The text blocks joined by newlines; empty when there are none.</summary>
    public string Text =>
        string.Join("\n", Content.OfType<JsonObject>().Where(b => Str(b["type"]) == "text").Select(b => Str(b["text"]) ?? ""));

    /// <summary>
    /// The assistant turn to append to the conversation: <c>{"role": "assistant", "content": [...]}</c>
    /// with a deep copy of every block exactly as received, thinking blocks and their signatures
    /// included, as the API requires when tool results follow.
    /// </summary>
    public JsonObject ToAssistantMessage() => new()
    {
        ["role"] = "assistant",
        ["content"] = Content.DeepClone(),
    };

    /// <summary>The speed the response says it ran at, <c>usage.speed</c>: "fast" or "standard".
    /// Null when the usage doesn't say.</summary>
    public string? Speed => ModelPrices.SpeedOf(UsageJson);

    /// <summary>The cost of this response, priced as <see cref="Model"/> when the response names
    /// one, else as <paramref name="requestedModel"/>, and at the speed its usage reports
    /// (<see cref="Speed"/>): the fast-mode rates for "fast", the standard ones for "standard".
    /// Only when the usage names no speed at all does <paramref name="requestedFast"/> decide,
    /// so a fast request whose response leaves the field out is not counted at half its price.</summary>
    public UsageCost Cost(string requestedModel, bool requestedFast = false) =>
        ModelPrices.Cost(Model ?? requestedModel, Usage, Speed is null ? requestedFast : ModelPrices.IsFast(UsageJson));

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
