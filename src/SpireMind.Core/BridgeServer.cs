using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace SpireMind.Core;

/// <summary>
/// Loopback-only HTTP server inside the game process.
///   POST /mcp     MCP endpoint for Claude Code
///   GET  /health  liveness and version
///   GET  /state   the current snapshot (handy with curl while debugging)
///   POST /act     {"action_id": "...", "version": n}, same as the act tool
/// </summary>
public sealed class BridgeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly McpEndpoint _mcp;
    private readonly BotTools _tools;
    private readonly string _version;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();

    public int Port { get; }

    public BridgeServer(int port, BotTools tools, string version, Action<string> log)
    {
        Port = port;
        _tools = tools;
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
                case ("POST", "/mcp"):
                    var reply = await _mcp.HandleAsync(await ReadBody(req), _stop.Token);
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

                case (_, "/mcp"):
                    // No server-initiated stream; the transport spec allows 405 here.
                    res.StatusCode = 405;
                    res.Headers["Allow"] = "POST";
                    res.Close();
                    return;

                case ("GET", "" or "/health"):
                    await Send(res, 200, new JsonObject { ["ok"] = true, ["name"] = "spiremind", ["version"] = _version });
                    return;

                case ("GET", "/state"):
                    var state = await _tools.CallAsync("get_state", new JsonObject(), _stop.Token);
                    await Send(res, state.IsError ? 500 : 200, state.Payload);
                    return;

                case ("POST", "/act"):
                    var args = JsonNode.Parse(await ReadBody(req)) as JsonObject ?? new JsonObject();
                    var acted = await _tools.CallAsync("act", args, _stop.Token);
                    await Send(res, acted.IsError ? 409 : 200, acted.Payload);
                    return;

                default:
                    await Send(res, 404, new JsonObject { ["error"] = $"no route {req.HttpMethod} {path}" });
                    return;
            }
        }
        catch (Exception ex)
        {
            _log($"request {req.HttpMethod} {req.Url?.AbsolutePath} failed: {ex}");
            try { await Send(res, 500, new JsonObject { ["error"] = ex.Message }); }
            catch { /* client already gone */ }
        }
    }

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
