using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireMind.Core;

/// <summary>
/// A minimal MCP server over the Streamable HTTP transport, answering every POST with a
/// plain JSON response (no SSE stream). That subset is enough for tools and is what lets
/// Claude Code talk to the mod directly with no Python or Node sidecar:
///   claude mcp add --transport http spiremind http://127.0.0.1:17772/mcp
/// A tool call the client gives up on (notifications/cancelled) is cancelled here, so an
/// abandoned wait_for_decision stops instead of taking chat messages nobody will read.
/// </summary>
public sealed class McpEndpoint
{
    public const string LatestProtocol = "2025-06-18";

    private static readonly string[] SupportedProtocols = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"];

    // Tool results are read by a model, not put in a web page, so ">" in action ids and "+" in
    // upgraded card names stay as they are instead of becoming \u003E and \u002B.
    private static readonly JsonSerializerOptions ModelJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly BotTools _tools;
    private readonly string _serverVersion;

    /// <summary>Tool calls in progress, by their JSON-RPC id as JSON text.</summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _calls = new();

    // Tool calls from clients other than the mod's own Claude Code: how many are running, and
    // when (UTC ticks) the last one started or ended; 0 when there never was one.
    private int _externalCalls;
    private long _externalLastTicks;

    public McpEndpoint(BotTools tools, string serverVersion)
    {
        _tools = tools;
        _serverVersion = serverVersion;
    }

    /// <summary>
    /// Handle one POST body. Returns the JSON to send back, or null when the body held only
    /// notifications/responses (the transport then answers 202 Accepted with no body).
    /// </summary>
    public Task<JsonNode?> HandleAsync(string body, CancellationToken ct) => HandleAsync(body, external: true, ct);

    /// <summary>True while a client other than the mod's own Claude Code has a tool call running,
    /// or had one within <paramref name="window"/>: somebody else is playing through the mod.</summary>
    public bool ExternalClientActive(TimeSpan window) =>
        Volatile.Read(ref _externalCalls) > 0
        || (Interlocked.Read(ref _externalLastTicks) is var last and > 0 && DateTime.UtcNow.Ticks - last < window.Ticks);

    /// <param name="external">False for the mod's own Claude Code (the subscription brain), whose
    /// calls don't count for <see cref="ExternalClientActive"/>.</param>
    public async Task<JsonNode?> HandleAsync(string body, bool external, CancellationToken ct)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(body);
        }
        catch (JsonException ex)
        {
            return ErrorResponse(null, -32700, $"Parse error: {ex.Message}");
        }

        if (parsed is JsonArray batch)
        {
            var replies = new JsonArray();
            foreach (var item in batch)
            {
                var reply = await HandleMessageAsync(item as JsonObject, external, ct);
                if (reply is not null) replies.Add(reply);
            }
            return replies.Count > 0 ? replies : null;
        }

        return await HandleMessageAsync(parsed as JsonObject, external, ct);
    }

    private async Task<JsonNode?> HandleMessageAsync(JsonObject? msg, bool external, CancellationToken ct)
    {
        if (msg is null)
            return ErrorResponse(null, -32600, "Invalid request");

        var id = msg["id"]?.DeepClone();
        var method = (msg["method"] as JsonValue)?.GetValue<string>();
        var @params = msg["params"] as JsonObject ?? new JsonObject();

        if (method == "notifications/cancelled")
        {
            Cancel(@params["requestId"]);
            return null;
        }

        // Notifications and client responses carry no reply.
        if (method is null || id is null)
            return null;

        switch (method)
        {
            case "initialize":
                return Result(id, Initialize(@params));
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, ListTools());
            case "tools/call":
                return await CallToolAsync(id, @params, external, ct);
            default:
                return ErrorResponse(id, -32601, $"Method not found: {method}");
        }
    }

    private JsonObject Initialize(JsonObject p)
    {
        var requested = (p["protocolVersion"] as JsonValue)?.GetValue<string>();
        var version = requested is not null && SupportedProtocols.Contains(requested) ? requested : LatestProtocol;
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = "spiremind", ["version"] = _serverVersion },
            ["instructions"] =
                "You are playing Slay the Spire 2 through these tools. Loop: wait_for_decision, think, act with an id " +
                "from snapshot.actions and its version. act returns the next snapshot, so you rarely need get_state.",
        };
    }

    private JsonObject ListTools()
    {
        var list = new JsonArray();
        foreach (var t in _tools.Specs)
        {
            list.Add(new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["inputSchema"] = t.InputSchema.DeepClone(),
            });
        }
        return new JsonObject { ["tools"] = list };
    }

    private async Task<JsonObject> CallToolAsync(JsonNode id, JsonObject p, bool external, CancellationToken ct)
    {
        if (external)
        {
            Interlocked.Increment(ref _externalCalls);
            Interlocked.Exchange(ref _externalLastTicks, DateTime.UtcNow.Ticks);
        }
        var name = (p["name"] as JsonValue)?.GetValue<string>() ?? "";
        var args = p["arguments"] as JsonObject ?? new JsonObject();

        var key = id.ToJsonString();
        using var call = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _calls[key] = call;
        ToolOutcome outcome;
        try
        {
            outcome = await _tools.CallAsync(name, args, call.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The client cancelled it, or a newer wait_for_decision replaced it. Nobody is
            // listening for this answer; it only closes the HTTP request.
            return ErrorResponse(id, -32800, "Request cancelled");
        }
        finally
        {
            _calls.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, call));
            if (external)
            {
                Interlocked.Exchange(ref _externalLastTicks, DateTime.UtcNow.Ticks);
                Interlocked.Decrement(ref _externalCalls);
            }
        }

        return Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = outcome.Payload.ToJsonString(ModelJson),
            }),
            ["isError"] = outcome.IsError,
        });
    }

    private void Cancel(JsonNode? requestId)
    {
        if (requestId is null || !_calls.TryGetValue(requestId.ToJsonString(), out var call))
            return;
        try { call.Cancel(); }
        catch (ObjectDisposedException) { /* it finished meanwhile */ }
    }

    private static JsonObject Result(JsonNode id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject ErrorResponse(JsonNode? id, int code, string message) =>
        new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
        };
}
