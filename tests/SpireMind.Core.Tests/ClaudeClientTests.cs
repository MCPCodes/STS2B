using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class ClaudeClientTests
{
    private const string Key = "sk-ant-api03-TESTKEY-0123456789abcdef";

    private const string OkBody = """
        {
          "id": "msg_01",
          "type": "message",
          "role": "assistant",
          "model": "claude-opus-5-5",
          "content": [
            { "type": "thinking", "thinking": "Bash first for Vulnerable.", "signature": "c2lnbmF0dXJl==" },
            { "type": "text", "text": "Bash, then Strikes." },
            { "type": "tool_use", "id": "toolu_01", "name": "act", "input": { "action_id": "play:3>e0", "version": 7 } }
          ],
          "stop_reason": "tool_use",
          "usage": {
            "input_tokens": 2000, "output_tokens": 800, "cache_read_input_tokens": 30000,
            "cache_creation_input_tokens": 500,
            "cache_creation": { "ephemeral_5m_input_tokens": 0, "ephemeral_1h_input_tokens": 500 }
          }
        }
        """;

    private static JsonObject Request() => new()
    {
        ["model"] = "claude-opus-5-5",
        ["max_tokens"] = 16000,
        ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Your turn." }),
    };

    private static HttpResponseMessage Reply(int status, string body, params (string Name, string Value)[] headers)
    {
        var reply = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers)
            reply.Headers.TryAddWithoutValidation(name, value);
        return reply;
    }

    private static HttpResponseMessage Ok() => Reply(200, OkBody, ("request-id", "req_ok"));

    private static string ErrorBody(string type, string message, string? errorCode = null)
    {
        var error = new JsonObject { ["type"] = type, ["message"] = message };
        if (errorCode is not null)
            error["details"] = new JsonObject { ["error_code"] = errorCode };
        return new JsonObject { ["type"] = "error", ["error"] = error, ["request_id"] = "req_err" }.ToJsonString();
    }

    private sealed class Rig
    {
        public readonly FakeHandler Handler = new();
        public readonly List<TimeSpan> Delays = [];
        public readonly List<string> Log = [];
        public double Random = 0;

        public ClaudeClient Client(string? baseUrl = null) => new(Key, Handler, baseUrl,
            delay: (wait, ct) =>
            {
                Delays.Add(wait);
                return Task.CompletedTask;
            },
            log: Log.Add,
            random: () => Random);
    }

    [Fact]
    public async Task Posts_the_request_with_key_and_version_headers()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Ok());
        var request = Request();
        var before = request.ToJsonString();

        var response = await rig.Client().SendAsync(request, CancellationToken.None);

        var sent = Assert.Single(rig.Handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", sent.Uri.ToString());
        Assert.Equal(Key, sent.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", sent.Headers["anthropic-version"]);
        Assert.False(sent.Headers.ContainsKey("anthropic-beta"));
        Assert.Equal("application/json", sent.ContentType);
        Assert.Equal(before, sent.Body);
        Assert.Equal(before, request.ToJsonString());

        Assert.Equal(1, response.Attempts);
        Assert.Equal("req_ok", response.RequestId);
        Assert.Equal("msg_01", response.Id);
        Assert.Equal("claude-opus-5-5", response.Model);
        Assert.Equal("tool_use", response.StopReason);
        Assert.Equal("Bash, then Strikes.", response.Text);
        Assert.Equal(3, response.Content.Count);
        var use = Assert.Single(response.ToolUses);
        Assert.Equal(new[] { "toolu_01", "act", "play:3>e0" }, new[] { use.Id, use.Name, use.Input["action_id"]!.GetValue<string>() });
        Assert.Equal(new TokenUsage(Input: 2000, Output: 800, CacheRead: 30000, CacheWrite5m: 0, CacheWrite1h: 500), response.Usage);
        Assert.Equal(0.034m, response.Cost("claude-sonnet-5-5").Usd);
        Assert.Empty(rig.Delays);
    }

    [Fact]
    public async Task Base_url_and_beta_headers_are_configurable()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Ok());
        var client = new ClaudeClient(Key, rig.Handler, "http://localhost:9999/") { Betas = ["compact-2026-01-12", "context-management-2025-06-27"] };

        await client.SendAsync(Request(), CancellationToken.None);

        var sent = Assert.Single(rig.Handler.Requests);
        Assert.Equal("http://localhost:9999/v1/messages", sent.Uri.ToString());
        Assert.Equal("compact-2026-01-12,context-management-2025-06-27", sent.Headers["anthropic-beta"]);
    }

    [Fact]
    public async Task Assistant_message_keeps_thinking_blocks_unmodified_and_detached()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Ok());
        var response = await rig.Client().SendAsync(Request(), CancellationToken.None);

        var message = response.ToAssistantMessage();
        Assert.Equal("assistant", message["role"]!.GetValue<string>());
        Assert.Equal(JsonNode.Parse(OkBody)!["content"]!.ToJsonString(), message["content"]!.ToJsonString());
        Assert.Equal("c2lnbmF0dXJl==", message["content"]![0]!["signature"]!.GetValue<string>());

        // A copy, so it can join the next request while the response stays intact.
        var conversation = new JsonArray(message);
        Assert.Single(conversation);
        Assert.Equal(3, response.Content.Count);
        response.ToolUses[0].Input["action_id"] = "changed";
        Assert.Equal("play:3>e0", response.Content[2]!["input"]!["action_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Rate_limit_429_waits_for_retry_after_then_retries()
    {
        var rig = new Rig();
        rig.Handler.Script(
            _ => Reply(429, ErrorBody("rate_limit_error", "Number of request tokens has exceeded your per-minute rate limit"),
                ("retry-after", "7"), ("request-id", "req_429")),
            _ => Ok());

        var response = await rig.Client().SendAsync(Request(), CancellationToken.None);

        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7)], rig.Delays);
        Assert.Equal(2, response.Attempts);
        Assert.Equal(rig.Handler.Requests[0].Body, rig.Handler.Requests[1].Body);
        var line = Assert.Single(rig.Log);
        Assert.Contains("429", line);
        Assert.Contains("req_429", line);
        Assert.DoesNotContain(Key, line);
    }

    [Fact]
    public async Task Fractional_retry_after_is_honoured()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(429, ErrorBody("rate_limit_error", "slow down"), ("retry-after", "1.5")), _ => Ok());
        await rig.Client().SendAsync(Request(), CancellationToken.None);
        Assert.Equal([TimeSpan.FromSeconds(1.5)], rig.Delays);
    }

    [Fact]
    public async Task Rate_limit_without_retry_after_backs_off()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(429, ErrorBody("rate_limit_error", "slow down")), _ => Ok());
        await rig.Client().SendAsync(Request(), CancellationToken.None);
        Assert.Equal([TimeSpan.FromSeconds(1)], rig.Delays);
    }

    [Fact]
    public async Task Rate_limit_asking_for_a_very_long_wait_is_not_retried()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(429, ErrorBody("rate_limit_error", "slow down"), ("retry-after", "3600")));

        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ClaudeErrorKind.RateLimited, ex.Kind);
        Assert.Equal(TimeSpan.FromHours(1), ex.RetryAfter);
        Assert.False(ex.NeedsHuman);
        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
    }

    [Fact]
    public async Task Spend_limit_429_is_not_retried()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(429,
            ErrorBody("rate_limit_error", "You have reached your spend limit.", errorCode: "enforced_spend_limit_reached"),
            ("retry-after", "30"), ("request-id", "req_spend")));

        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ClaudeErrorKind.SpendLimit, ex.Kind);
        Assert.Equal(429, ex.Status);
        Assert.Equal("rate_limit_error", ex.ErrorType);
        Assert.Equal("req_spend", ex.RequestId);
        Assert.True(ex.NeedsHuman);
        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
    }

    [Theory]
    [InlineData(402, "billing_error", "Payment required.", ClaudeErrorKind.OutOfCredit)]
    [InlineData(400, "invalid_request_error",
        "Your credit balance is too low to access the Anthropic API. Please go to Plans & Billing to upgrade or purchase credits.",
        ClaudeErrorKind.OutOfCredit)]
    [InlineData(400, "invalid_request_error",
        "You have reached your specified API usage limits. You will regain access on 2026-11-01 at 00:00 UTC.",
        ClaudeErrorKind.SpendLimit)]
    [InlineData(401, "authentication_error", "invalid x-api-key", ClaudeErrorKind.Unauthorized)]
    [InlineData(403, "permission_error", "Your API key does not have permission to use the specified resource.", ClaudeErrorKind.Unauthorized)]
    [InlineData(400, "invalid_request_error", "tool_choice any is not supported with this model", ClaudeErrorKind.InvalidRequest)]
    [InlineData(404, "not_found_error", "model: claude-opus-9", ClaudeErrorKind.InvalidRequest)]
    [InlineData(413, "request_too_large", "Request exceeds the maximum allowed number of bytes.", ClaudeErrorKind.InvalidRequest)]
    [InlineData(418, "teapot", "short and stout", ClaudeErrorKind.Other)]
    [InlineData(501, "api_error", "not implemented", ClaudeErrorKind.Server)]
    public async Task Final_errors_are_classified_without_retrying(int status, string type, string message, ClaudeErrorKind kind)
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(status, ErrorBody(type, message), ("request-id", "req_x")));

        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));

        Assert.Equal(kind, ex.Kind);
        Assert.Equal(status, ex.Status);
        Assert.Equal(type, ex.ErrorType);
        Assert.Equal(message, ex.ApiMessage);
        Assert.Equal("req_x", ex.RequestId);
        Assert.Equal(1, ex.Attempts);
        Assert.Contains($"HTTP {status}", ex.Message);
        Assert.Contains("req_x", ex.Message);
        Assert.Equal(kind is ClaudeErrorKind.OutOfCredit or ClaudeErrorKind.SpendLimit or ClaudeErrorKind.Unauthorized, ex.NeedsHuman);
        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
    }

    [Fact]
    public async Task Prompt_too_long_is_flagged()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(400, ErrorBody("invalid_request_error", "prompt is too long: 1000123 tokens > 1000000 maximum")));
        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));
        Assert.Equal(ClaudeErrorKind.InvalidRequest, ex.Kind);
        Assert.True(ex.PromptTooLong);
    }

    [Fact]
    public async Task Error_without_a_json_body_keeps_the_text()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(403, "<html>Forbidden by proxy</html>"));
        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));
        Assert.Equal(ClaudeErrorKind.Unauthorized, ex.Kind);
        Assert.Null(ex.ErrorType);
        Assert.Equal("<html>Forbidden by proxy</html>", ex.ApiMessage);
    }

    [Fact]
    public async Task Overload_backs_off_exponentially_and_gives_up_after_five_tries()
    {
        var rig = new Rig { Random = 0.5 };
        rig.Handler.Script(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(
            _ => Reply(529, ErrorBody("overloaded_error", "Overloaded"), ("request-id", "req_529")), 10).ToArray());

        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ClaudeErrorKind.Overloaded, ex.Kind);
        Assert.Equal(529, ex.Status);
        Assert.Equal(ClaudeClient.MaxAttempts, ex.Attempts);
        Assert.Contains("after 5 tries", ex.Message);
        Assert.Equal(5, rig.Handler.Requests.Count);
        // 1, 2, 4, 8 seconds plus a quarter (half of the 0.5 the jitter source returned).
        Assert.Equal([1.25, 2.5, 5, 10], rig.Delays.Select(d => d.TotalSeconds));
        Assert.Equal(4, rig.Log.Count);
    }

    [Fact]
    public async Task Jitter_stays_within_half_of_the_base_wait()
    {
        var rig = new Rig { Random = 0.999999 };
        rig.Handler.Script(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(
            _ => Reply(503, ErrorBody("api_error", "unavailable")), 5).ToArray());

        await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));

        var bases = new[] { 1.0, 2, 4, 8 };
        Assert.Equal(4, rig.Delays.Count);
        for (var i = 0; i < 4; i++)
            Assert.InRange(rig.Delays[i].TotalSeconds, bases[i], bases[i] * 1.5);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(529)]
    public async Task Server_errors_are_retried(int status)
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(status, ErrorBody("api_error", "Internal server error")), _ => Ok());

        var response = await rig.Client().SendAsync(Request(), CancellationToken.None);

        Assert.Equal(2, response.Attempts);
        Assert.Equal([TimeSpan.FromSeconds(1)], rig.Delays);
    }

    [Fact]
    public async Task Server_errors_that_persist_say_server()
    {
        var rig = new Rig();
        rig.Handler.Script(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(
            _ => Reply(500, ErrorBody("api_error", "Internal server error")), 5).ToArray());
        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));
        Assert.Equal(ClaudeErrorKind.Server, ex.Kind);
        Assert.Equal(5, ex.Attempts);
    }

    [Fact]
    public async Task Network_errors_are_retried()
    {
        var rig = new Rig();
        rig.Handler.Script(
            _ => throw new HttpRequestException("Connection refused (api.anthropic.com:443)"),
            _ => throw new HttpRequestException("The SSL connection could not be established"),
            _ => Ok());

        var response = await rig.Client().SendAsync(Request(), CancellationToken.None);

        Assert.Equal(3, response.Attempts);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], rig.Delays);
    }

    [Fact]
    public async Task Persistent_network_errors_end_as_network()
    {
        var rig = new Rig();
        rig.Handler.Script(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(
            _ => throw new HttpRequestException("Name or service not known"), 5).ToArray());

        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ClaudeErrorKind.Network, ex.Kind);
        Assert.Null(ex.Status);
        Assert.Equal(5, ex.Attempts);
        Assert.Contains("Name or service not known", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task A_timeout_not_caused_by_the_caller_is_retried()
    {
        var rig = new Rig();
        rig.Handler.Script(
            _ => throw new TaskCanceledException("The request was canceled due to the configured timeout.", new TimeoutException()),
            _ => Ok());

        var response = await rig.Client().SendAsync(Request(), CancellationToken.None);

        Assert.Equal(2, response.Attempts);
        Assert.Contains("timeout", Assert.Single(rig.Log));
    }

    [Fact]
    public async Task A_garbled_success_body_is_retried()
    {
        var rig = new Rig();
        rig.Handler.Script(_ => Reply(200, "{\"id\": \"msg_01\", \"content\": [{\"ty"), _ => Ok());
        var response = await rig.Client().SendAsync(Request(), CancellationToken.None);
        Assert.Equal(2, response.Attempts);
    }

    [Fact]
    public async Task Cancelling_during_the_backoff_stops_at_once()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHandler();
        handler.Script(_ => Reply(529, ErrorBody("overloaded_error", "Overloaded")), _ => Ok());
        var client = new ClaudeClient(Key, handler, delay: (_, ct) =>
        {
            cts.Cancel();
            return Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(Request(), cts.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Cancelling_during_a_request_is_not_retried()
    {
        using var cts = new CancellationTokenSource();
        var rig = new Rig();
        rig.Handler.Script(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        }, _ => Ok());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Client().SendAsync(Request(), cts.Token));
        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
    }

    [Fact]
    public async Task The_key_never_shows_in_exceptions_or_logs()
    {
        var rig = new Rig();
        // Contrived: an error message and a network error that echo the key.
        rig.Handler.Script(
            _ => throw new HttpRequestException($"proxy rejected header x-api-key: {Key}"),
            _ => Reply(401, ErrorBody("authentication_error", $"invalid x-api-key {Key}")));

        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ClaudeErrorKind.Unauthorized, ex.Kind);
        Assert.DoesNotContain(Key, ex.Message);
        Assert.DoesNotContain(Key, ex.ApiMessage);
        Assert.Contains("[redacted]", ex.ApiMessage);
        Assert.DoesNotContain(Key, ex.ToString());
        Assert.All(rig.Log, line => Assert.DoesNotContain(Key, line));
        Assert.Contains("[redacted]", Assert.Single(rig.Log));
    }

    [Fact]
    public async Task The_key_never_shows_through_an_inner_exception()
    {
        var rig = new Rig();
        rig.Handler.Script(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(
            _ => throw new HttpRequestException($"proxy said no to {Key}"), 5).ToArray());

        var ex = await Assert.ThrowsAsync<ClaudeApiException>(() => rig.Client().SendAsync(Request(), CancellationToken.None));

        Assert.Equal(ClaudeErrorKind.Network, ex.Kind);
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain(Key, ex.ToString());
        Assert.Equal(4, rig.Log.Count);
        Assert.All(rig.Log, line => Assert.DoesNotContain(Key, line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sk-ant key with spaces")]
    [InlineData("\"sk-ant-quoted\"")]
    [InlineData("sk-ant-été")]
    public void A_malformed_key_is_refused_without_showing_it(string key)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ClaudeClient(key, new FakeHandler()));
        if (key.Length > 0)
            Assert.DoesNotContain(key, ex.Message);
    }

    [Fact]
    public async Task Concurrent_calls_share_one_client()
    {
        var handler = new FakeHandler();
        handler.Script(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(_ => Ok(), 20).ToArray());
        using var client = new ClaudeClient(Key, handler, delay: (_, _) => Task.CompletedTask);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => client.SendAsync(Request(), CancellationToken.None))));

        Assert.All(responses, r => Assert.Equal("tool_use", r.StopReason));
        Assert.Equal(20, handler.Requests.Count);
    }

    [Fact]
    public void Each_try_waits_longer_for_a_larger_max_tokens()
    {
        using var client = new ClaudeClient(Key, new FakeHandler());

        Assert.Equal(TimeSpan.FromMinutes(10), client.TimeoutFor(new JsonObject()));
        Assert.Equal(TimeSpan.FromMinutes(10), client.TimeoutFor(Request()));
        Assert.Equal(TimeSpan.FromMinutes(15), client.TimeoutFor(new JsonObject { ["max_tokens"] = 32_000 }));
        Assert.Equal(TimeSpan.FromMinutes(30), client.TimeoutFor(JsonNode.Parse("""{"max_tokens": 64000}""")!.AsObject()));
        Assert.Equal(TimeSpan.FromHours(1), client.TimeoutFor(new JsonObject { ["max_tokens"] = 128_000 }));

        using var patient = new ClaudeClient(Key, new FakeHandler()) { RequestTimeout = TimeSpan.FromMinutes(45) };
        Assert.Equal(TimeSpan.FromMinutes(45), patient.TimeoutFor(new JsonObject { ["max_tokens"] = 64_000 }));
    }

    [Fact]
    public async Task The_clients_own_connections_work_with_keep_alive()
    {
        // No handler passed, so the client connects through its own keep-alive callback. A bare
        // HTTP/1.1 server on loopback answers once.
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var served = Task.Run(async () =>
            {
                using var tcp = await listener.AcceptTcpClientAsync();
                using var stream = tcp.GetStream();
                var buffer = new byte[64 * 1024];
                var read = 0;
                var text = "";
                while (!text.Contains("\r\n\r\n") || read < HeaderEnd(text) + ContentLength(text))
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(read));
                    if (n == 0) break;
                    read += n;
                    text = Encoding.UTF8.GetString(buffer, 0, read);
                }
                var body = Encoding.UTF8.GetBytes(OkBody);
                var head = $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                await stream.WriteAsync(body);
                return text;
            });

            using var client = new ClaudeClient(Key, baseUrl: $"http://127.0.0.1:{port}");
            var response = await client.SendAsync(Request(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("tool_use", response.StopReason);
            var request = await served.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.StartsWith("POST /v1/messages HTTP/1.1", request);
        }
        finally
        {
            listener.Stop();
        }

        static int HeaderEnd(string text) => text.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;

        static int ContentLength(string text)
        {
            foreach (var line in text[..HeaderEnd(text)].Split("\r\n"))
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    return int.Parse(line["Content-Length:".Length..].Trim());
            return 0;
        }
    }

    /// <summary>Records each request and answers from a script, one entry per request. An entry
    /// may throw to simulate a network failure.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _script = new();
        private readonly List<Sent> _requests = [];

        public sealed record Sent(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? ContentType, string Body);

        public IReadOnlyList<Sent> Requests
        {
            get { lock (_gate) return _requests.ToArray(); }
        }

        public void Script(params Func<HttpRequestMessage, HttpResponseMessage>[] replies)
        {
            lock (_gate)
                foreach (var reply in replies)
                    _script.Enqueue(reply);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            var headers = request.Headers.ToDictionary(h => h.Key.ToLowerInvariant(), h => string.Join(",", h.Value));
            Func<HttpRequestMessage, HttpResponseMessage> next;
            lock (_gate)
            {
                _requests.Add(new Sent(request.Method, request.RequestUri!, headers, request.Content?.Headers.ContentType?.ToString(), body));
                next = _script.Count > 0 ? _script.Dequeue() : throw new InvalidOperationException("No scripted reply left.");
            }
            return next(request);
        }
    }
}
