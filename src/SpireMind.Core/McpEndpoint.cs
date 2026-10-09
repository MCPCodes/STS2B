using System.Collections.Concurrent;
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

    private readonly BotTools _tools;
    private readonly string _serverVersion;

    /// <summary>Tool calls in progress, by their JSON-RPC id as JSON text.</summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _calls = new();

    public McpEndpoint(BotTools tools, string serverVersion)
    {
        _tools = tools;
        _serverVersion = serverVersion;
    }

    /// <summary>
    /// Handle one POST body. Returns the JSON to send back, or null when the body held only
    /// notifications/responses (the transport then answers 202 Accepted with no body).
    /// </summary>
    public async Task<JsonNode?> HandleAsync(string body, CancellationToken ct)
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
                var reply = await HandleMessageAsync(item as JsonObject, ct);
                if (reply is not null) replies.Add(reply);
            }
            return replies.Count > 0 ? replies : null;
        }

        return await HandleMessageAsync(parsed as JsonObject, ct);
    }

    private async Task<JsonNode?> HandleMessageAsync(JsonObject? msg, CancellationToken ct)
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
                return await CallToolAsync(id, @params, ct);
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

    private async Task<JsonObject> CallToolAsync(JsonNode id, JsonObject p, CancellationToken ct)
    {
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
        }

        return Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = outcome.Payload.ToJsonString(),
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
