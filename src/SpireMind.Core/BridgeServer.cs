using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
// An alias rather than the namespace, so a game type of the same name in the mod's global
// usings can never make it ambiguous.
using InstanceInfo = SpireMind.Core.Coop.InstanceInfo;

namespace SpireMind.Core;

/// <summary>
/// Loopback-only HTTP server inside the game process.
///   POST /mcp     MCP endpoint for Claude Code
///   POST /mcp/own the same endpoint for the Claude Code the mod starts itself (the subscription brain)
///   GET  /health  liveness and version
///   GET  /state   the current snapshot (handy with curl while debugging)
///   POST /act     {"action_id": "...", "version": n}, same as the act tool
///   POST /chat    {"from": "Mark", "text": "..."}, posts as the in-game chat box does;
///                 with "teammate": true it is another bot's line relayed in (party chat)
///   GET  /chat?since=n  the chat log after line n, with the bot's name, the pause flag and who this copy is
///   GET  /coop/info  who this copy is, for the co-op relay: role, port, instance, session, NetId, character
///   GET  /debug/screenshot  saves what the game is drawing to a PNG and returns its path (when the host offers it)
/// /state and /act go through the tools and show unread chat messages like them, but leave
/// them unread, so a curl doesn't take them from the model.
/// </summary>
public sealed class BridgeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly McpEndpoint _mcp;
    private readonly BotTools _tools;
    private readonly ChatHub _chat;
    private readonly string _version;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();

    public int Port { get; }

    /// <summary>True while an MCP client on /mcp (not the mod's own Claude Code on /mcp/own) has a
    /// tool call running or had one within <paramref name="window"/>.</summary>
    public bool ExternalClientActive(TimeSpan window) => _mcp.ExternalClientActive(window);

    /// <summary>
    /// Saves a picture of the game's screen and returns where, for checking by eye what the
    /// overlay and bubbles look like. Set by the host; null means the route answers 404.
    /// </summary>
    public Func<CancellationToken, Task<JsonObject>>? DebugScreenshot { get; set; }

    /// <summary>What this copy reports about itself on /coop/info and /chat. Null reports a
    /// human copy with instance "main" and no NetId.</summary>
    public InstanceInfo? Instance { get; init; }

    /// <summary>A new id for every server (so every launch of the game), which tells the co-op
    /// relay that a copy on a port it knew was restarted and its chat log began again.</summary>
    public string Session { get; }

    public BridgeServer(int port, BotTools tools, ChatHub chat, string version, Action<string> log)
    {
        Port = port;
        Session = Guid.NewGuid().ToString("N");
        _tools = tools;
        _chat = chat;
        _version = version;
        _log = log;
        _mcp = new McpEndpoint(tools, version);
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "SpireMind.Bridge" };
        thread.Start();
        _log($"bridge listening on http://127.0.0.1:{Port}/ (MCP at /mcp)");
    }

    private void AcceptLoop()
    {
        while (!_stop.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = _listener.GetContext();
            }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException ex)
            {
                _log($"accept failed: {ex.Message}");
                continue;
            }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            // Browsers send Origin; MCP's spec asks local servers to reject foreign ones
            // so a web page can't drive the game via DNS rebinding.
            if (!IsLocalOrigin(req.Headers["Origin"]))
            {
                await Send(res, 403, new JsonObject { ["error"] = "forbidden origin" });
                return;
            }

            var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "";
            switch (req.HttpMethod, path)
            {
                case ("POST", "/mcp" or "/mcp/own"):
                    var reply = await _mcp.HandleAsync(await ReadBody(req), external: path == "/mcp", _stop.Token);
                    if (reply is null)
                    {
                        res.StatusCode = 202;
                        res.Close();
                    }
                    else
                    {
                        await Send(res, 200, reply);
                    }
                    return;

                case (_, "/mcp" or "/mcp/own"):
                    // No server-initiated stream; the transport spec allows 405 here.
                    res.StatusCode = 405;
                    res.Headers["Allow"] = "POST";
                    res.Close();
                    return;

                case ("GET", "" or "/health"):
                    await Send(res, 200, new JsonObject { ["ok"] = true, ["name"] = "spiremind", ["version"] = _version });
                    return;

                case ("GET", "/state"):
                    var state = await _tools.CallAsync("get_state", new JsonObject(), takeChat: false, _stop.Token);
                    await Send(res, state.IsError ? 500 : 200, state.Payload);
                    return;

                case ("POST", "/act"):
                    var args = JsonNode.Parse(await ReadBody(req)) as JsonObject ?? new JsonObject();
                    var acted = await _tools.CallAsync("act", args, takeChat: false, _stop.Token);
                    await Send(res, acted.IsError ? 409 : 200, acted.Payload);
                    return;

                case ("POST", "/chat"):
                    var message = JsonNode.Parse(await ReadBody(req)) as JsonObject ?? new JsonObject();
                    var teammate = message["teammate"] is JsonValue tv && tv.TryGetValue<bool>(out var tm) && tm;
                    var posted = teammate
                        ? _chat.PostFromTeammate(Text(message["from"]) ?? "Bot", Text(message["text"]) ?? "")
                        : _chat.PostFromHuman(Text(message["from"]) ?? "Player", Text(message["text"]) ?? "");
                    await Send(res, posted.Outcome == PostOutcome.Empty ? 400 : 200, new JsonObject
                    {
                        ["outcome"] = posted.Outcome switch
                        {
                            PostOutcome.Queued => "queued",
                            PostOutcome.Command => "command",
                            PostOutcome.NotForMe => "not_for_me",
                            _ => "empty",
                        },
                        ["reply"] = posted.Reply,
                        ["seq"] = posted.Seq,
                    });
                    return;

                case ("GET", "/chat"):
                    var since = long.TryParse(req.QueryString["since"], out var n) ? n : 0;
                    await Send(res, 200, ChatLog(since));
                    return;

                case ("GET", "/coop/info"):
                    var info = new JsonObject { ["name"] = "spiremind", ["version"] = _version };
                    AddIdentity(info);
                    info["paused"] = _chat.Paused;
                    await Send(res, 200, info);
                    return;

                case ("GET", "/debug/screenshot") when DebugScreenshot is { } shoot:
                    await Send(res, 200, await shoot(_stop.Token));
                    return;

                default:
                    await Send(res, 404, new JsonObject { ["error"] = $"no route {req.HttpMethod} {path}" });
                    return;
            }
        }
        catch (JsonException ex)
        {
            // A bad body on /act or /chat is the caller's mistake, not a server failure.
            try { await Send(res, 400, new JsonObject { ["error"] = $"body must be a JSON object: {ex.Message}" }); }
            catch { /* client already gone */ }
        }
        catch (Exception ex)
        {
            _log($"request {req.HttpMethod} {req.Url?.AbsolutePath} failed: {ex}");
            try { await Send(res, 500, new JsonObject { ["error"] = ex.Message }); }
            catch { /* client already gone */ }
        }
    }

    private JsonObject ChatLog(long since)
    {
        // last_seq is read before the lines, so it is never past the last line in this reply.
        var log = new JsonObject { ["bot"] = _chat.BotName, ["paused"] = _chat.Paused };
        AddIdentity(log);
        var lines = new JsonArray();
        foreach (var l in _chat.LinesSince(since))
        {
            lines.Add(new JsonObject
            {
                ["seq"] = l.Seq,
                ["kind"] = l.Kind.ToString().ToLowerInvariant(),
                ["from"] = l.From,
                ["text"] = l.Text,
                ["remote"] = l.Remote,
            });
        }
        log["lines"] = lines;
        return log;
    }

    // Who this copy is, for the co-op relay: the same fields on /coop/info and /chat (playing
    // too, so the relay's /bots stays current from its polls).
    private void AddIdentity(JsonObject json)
    {
        json["role"] = Instance?.IsBot == true ? "bot" : "human";
        json["port"] = Port;
        json["instance"] = Instance?.InstanceId ?? "main";
        json["session"] = Session;
        json["net_id"] = Instance?.NetId;
        json["character"] = _chat.BotName;
        json["playing"] = Instance?.ReadPlaying();
        json["last_seq"] = _chat.LastSeq;
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool IsLocalOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin) || origin == "null") return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && (uri.Host is "127.0.0.1" or "localhost" or "[::1]" or "::1");
    }

    private static async Task<string> ReadBody(HttpListenerRequest req)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task Send(HttpListenerResponse res, int status, JsonNode body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.ToJsonString());
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); _listener.Close(); }
        catch { /* shutting down */ }
    }
}
