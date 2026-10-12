using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// A scripted stand-in for the Claude Messages API, for trying <c>--brain api</c> without a key
/// and without spending anything (<c>--fake-api</c>, and the checks): an HTTP transport that
/// Core's <see cref="ClaudeClient"/> is given in place of the network. Everything above it is
/// real: the client builds and sends the request, the brain builds its prompt, runs the tool
/// calls that come back and counts the cost. Nothing here reaches a network.
/// <para>
/// It answers each request with tool calls made from the last snapshot the request shows, the
/// way <c>fake-claude.py</c> plays for Claude Code: the first card it may play, a card choice
/// confirmed as soon as it can be, else end the turn. So it gets through any fight and loses
/// the hard ones. The model name picks what else it does:
/// </para>
/// <list type="bullet">
/// <item><c>fake-play</c>: plays, one <c>act</c> at a time.</item>
/// <item><c>fake-notes</c> (and any name that doesn't start with "fake-"): also calls
/// <c>get_persona</c>, reads the first enemy's wiki page and writes a journal line before its
/// first move, and once the fight is over holds a lesson for that page and one for
/// strategy/learned.md and adds a journal line.</item>
/// <item><c>fake-pause</c>: ends its turn once without a tool call after two moves; the brain's
/// own "Keep playing" brings it back.</item>
/// <item><c>fake-quit</c>: makes one move and then only ever answers in words, so the brain
/// gives up on it: an abandoned fight.</item>
/// <item><c>fake-401</c>: every request is refused as a bad key, which stops the batch.</item>
/// </list>
/// <para>
/// The usage it reports is fixed (a cache write of 10,000 tokens on a conversation's first
/// request, a cache read of as many after it), so a fight's cost follows from its request count.
/// </para>
/// </summary>
public sealed class FakeApi : HttpMessageHandler
{
    /// <summary>The key the client is made with when this is its transport. It opens nothing.</summary>
    public const string Key = "fake-key-for-the-arena-stand-in";

    public const string Play = "fake-play", Notes = "fake-notes", Pause = "fake-pause", Quit = "fake-quit", BadKey = "fake-401";

    private readonly object _gate = new();
    private readonly List<JsonObject> _requests = [];
    private int _count;

    /// <summary>True keeps every request body for <see cref="Requests"/>; the checks read them.</summary>
    public bool Keep { get; init; }

    /// <summary>The request bodies so far, in order (only with <see cref="Keep"/>).</summary>
    public IReadOnlyList<JsonObject> Requests { get { lock (_gate) return _requests.ToList(); } }

    /// <summary>Requests answered so far.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>The anthropic-beta header of each kept request, or null where there was none.</summary>
    public List<string?> Betas { get; } = [];

    /// <summary>True when every request so far carried <see cref="Key"/> and no other key.</summary>
    public bool OnlyTheFakeKey { get; private set; } = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var json = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
        var number = Interlocked.Increment(ref _count);
        lock (_gate)
        {
            if (!request.Headers.TryGetValues("x-api-key", out var keys) || keys.Any(key => key != Key))
                OnlyTheFakeKey = false;
            if (Keep)
            {
                _requests.Add(json);
                Betas.Add(request.Headers.TryGetValues("anthropic-beta", out var betas) ? string.Join(",", betas) : null);
            }
        }

        var model = Str(json["model"]) ?? "";
        if (model == BadKey)
            return Json(HttpStatusCode.Unauthorized, new JsonObject
            {
                ["type"] = "error",
                ["error"] = new JsonObject { ["type"] = "authentication_error", ["message"] = "invalid x-api-key (the stand-in's fake-401)" },
            }, number);

        var content = Reply(json, model.StartsWith("fake-", StringComparison.Ordinal) ? model : Notes);
        var first = (json["messages"] as JsonArray)?.Count == 1;
        return Json(HttpStatusCode.OK, new JsonObject
        {
            ["id"] = string.Create(CultureInfo.InvariantCulture, $"msg_fake_{number}"),
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = model,
            ["content"] = content,
            ["stop_reason"] = content.OfType<JsonObject>().Any(block => Str(block["type"]) == "tool_use") ? "tool_use" : "end_turn",
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = first ? 1000 : 300,
                ["output_tokens"] = 200,
                ["cache_read_input_tokens"] = first ? 0 : 10_000,
                ["cache_creation_input_tokens"] = first ? 10_000 : 0,
                ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = 0, ["ephemeral_1h_input_tokens"] = first ? 10_000 : 0 },
            },
        }, number);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, JsonObject body, int number)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("request-id", string.Create(CultureInfo.InvariantCulture, $"req_fake_{number}"));
        return response;
    }

    // The reply's content blocks: a thinking block when the request asked for thinking (the brain
    // must hand it back untouched), then words or tool calls.
    private static JsonArray Reply(JsonObject request, string behaviour)
    {
        var messages = (request["messages"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var content = new JsonArray();
        if (request["thinking"] is not null)
            content.Add(new JsonObject { ["type"] = "thinking", ["thinking"] = "The stand-in picks the first card it may play.", ["signature"] = "fake-signature" });

        var uses = messages.Where(m => Str(m["role"]) == "assistant")
            .SelectMany(m => (m["content"] as JsonArray ?? []).OfType<JsonObject>())
            .Where(block => Str(block["type"]) == "tool_use")
            .Select(block => (Name: Str(block["name"]) ?? "", Input: block["input"] as JsonObject ?? new JsonObject()))
            .ToList();
        var opening = FirstSnapshot(messages);
        var snapshot = LatestSnapshot(messages);
        var calls = 0;
        void Call(string name, JsonObject input) =>
            content.Add(new JsonObject
            {
                ["type"] = "tool_use",
                ["id"] = string.Create(CultureInfo.InvariantCulture, $"toolu_fake_{messages.Count}_{calls++}"),
                ["name"] = name,
                ["input"] = input,
            });
        void Say(string text) => content.Add(new JsonObject { ["type"] = "text", ["text"] = text });

        var enemy = Str((opening?["state"]?["enemies"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["name"]);
        var page = $"{NotesTools.WikiFolder}/enemies/{Slug(enemy ?? "unknown enemy")}.md";
        var floor = opening?["state"]?["run"]?["floor"]?.ToJsonString() ?? "?";

        // Before the first move.
        if (uses.Count == 0)
        {
            if (behaviour == Notes)
            {
                Call("get_persona", new JsonObject());
                Call("notes_read", new JsonObject { ["file"] = page });
                Call("notes_append", new JsonObject { ["file"] = NotesTools.Journal, ["content"] = $"- F{floor}: fight against {enemy ?? "an unknown enemy"} started." });
            }
            else
            {
                Call("wait_for_decision", new JsonObject());
            }
            return content;
        }

        // The end of the fight.
        if (Str(snapshot?["screen"]) == ArenaGame.OverScreen)
        {
            var result = Str(snapshot!["state"]?["result"]) ?? "over";
            if (behaviour == Notes && !uses.Any(use => use.Name == "notes_append" && Str(use.Input["file"]) == NotesTools.Learned))
            {
                var date = Today(messages);
                Call("notes_append", new JsonObject { ["file"] = page, ["content"] = $"- (Ironclad, {date}, floor {floor}) The stand-in {result} this fight playing its first card first." });
                Call("notes_append", new JsonObject { ["file"] = NotesTools.Learned, ["content"] = $"- (Ironclad, {date}, floor {floor}) A stand-in's lesson, held for review like any other." });
                Call("notes_append", new JsonObject { ["file"] = NotesTools.Journal, ["content"] = $"- F{floor}: {result}." });
            }
            else
            {
                Say($"The fight is over: {result}.");
            }
            return content;
        }

        var acts = uses.Count(use => use.Name == "act");
        var nudged = messages.Where(m => Str(m["role"]) == "user")
            .SelectMany(m => (m["content"] as JsonArray ?? []).OfType<JsonObject>())
            .Any(block => Str(block["type"]) == "text" && (Str(block["text"]) ?? "").StartsWith("Keep playing", StringComparison.Ordinal));
        if (behaviour == Quit && acts >= 1)
        {
            Say("I'll leave it here.");
            return content;
        }
        if (behaviour == Pause && acts == 2 && !nudged)
        {
            Say("Let me think about this.");
            return content;
        }

        if (snapshot is null || !(snapshot["awaiting_decision"] is JsonValue awaiting && awaiting.TryGetValue<bool>(out var deciding) && deciding))
        {
            Call("wait_for_decision", new JsonObject());
            return content;
        }

        var ids = (snapshot["actions"] as JsonArray ?? []).OfType<JsonObject>().Select(action => Str(action["id"])).OfType<string>().ToList();
        var pick = ids.FirstOrDefault(id => id.StartsWith("play:", StringComparison.Ordinal))
                   ?? ids.FirstOrDefault(id => id == "confirm")
                   ?? ids.FirstOrDefault(id => id.StartsWith("select:", StringComparison.Ordinal) || id.StartsWith("card:", StringComparison.Ordinal))
                   ?? ids.FirstOrDefault(id => id == "end_turn")
                   ?? ids.FirstOrDefault();
        if (pick is null)
            Call("wait_for_decision", new JsonObject());
        else
            Call("act", new JsonObject { ["action_id"] = pick, ["version"] = snapshot["version"]?.DeepClone() });
        return content;
    }

    // The newest snapshot a request shows: in a tool result (the snapshot itself, or the next or
    // current one of an act), or in the brain's own text (the state of an opening message, or
    // after "Keep playing"). Looked for from the last user message back.
    private static JsonObject? LatestSnapshot(List<JsonObject> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (Str(messages[i]["role"]) != "user" || messages[i]["content"] is not JsonArray blocks)
                continue;
            for (var b = blocks.Count - 1; b >= 0; b--)
                if (blocks[b] is JsonObject block && SnapshotIn(block) is { } snapshot)
                    return snapshot;
        }
        return null;
    }

    // The state the conversation's first message shows.
    private static JsonObject? FirstSnapshot(List<JsonObject> messages) =>
        messages.Count > 0 && messages[0]["content"] is JsonArray blocks
            ? blocks.OfType<JsonObject>().Select(SnapshotIn).FirstOrDefault(snapshot => snapshot is not null)
            : null;

    private static JsonObject? SnapshotIn(JsonObject block)
    {
        switch (Str(block["type"]))
        {
            case "tool_result" when Str(block["content"]) is { } result:
                return Parse(result) is { } payload ? Shown(payload) : null;
            case "text" when Str(block["text"]) is { } text:
                const string state = "## Current state\n\n";
                var at = text.IndexOf(state, StringComparison.Ordinal);
                if (at >= 0)
                {
                    var json = text[(at + state.Length)..];
                    var end = json.LastIndexOf("\n\n" + ApiBrain.CarryOn, StringComparison.Ordinal);
                    return Parse(end >= 0 ? json[..end] : json) is { } first ? Shown(first) : null;
                }
                if (text.StartsWith(ApiBrain.KeepPlayingWithState, StringComparison.Ordinal))
                    return Parse(text[ApiBrain.KeepPlayingWithState.Length..]) is { } waited ? Shown(waited) : null;
                return null;
            default:
                return null;
        }
    }

    private static JsonObject? Shown(JsonObject payload) =>
        payload.ContainsKey("screen") ? payload
        : payload["next"] is JsonObject next && next.ContainsKey("screen") ? next
        : payload["current"] is JsonObject current && current.ContainsKey("screen") ? current
        : null;

    private static JsonObject? Parse(string text)
    {
        try { return JsonNode.Parse(text.Trim()) as JsonObject; }
        catch (JsonException) { return null; }
    }

    // The date the brain's opening message gives ("Today is 2026-10-11."), for a lesson line.
    private static string Today(List<JsonObject> messages)
    {
        const string today = "Today is ";
        var text = messages.Count > 0 && messages[0]["content"] is JsonArray blocks
            ? string.Join("\n", blocks.OfType<JsonObject>().Select(block => Str(block["text"]) ?? ""))
            : "";
        var at = text.IndexOf(today, StringComparison.Ordinal);
        return at >= 0 && text.Length >= at + today.Length + 10
            ? text.Substring(at + today.Length, 10)
            : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // "Kin Priest" as a wiki page's file name: kin-priest.
    private static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        return slug.ToString().Trim('-') is { Length: > 0 } made ? made : "unknown";
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
