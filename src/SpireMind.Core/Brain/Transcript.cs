using System.Text.Json;
using System.Text.Json.Nodes;
// The mod compiles Core with global usings for Godot, which has its own FileAccess.
using FileAccess = System.IO.FileAccess;

namespace SpireMind.Core.Brain;

/// <summary>
/// The brain's conversations as JSON lines in <c>&lt;dir&gt;/transcripts/&lt;yyyy-MM-dd&gt;.jsonl</c>
/// (UTC dates), for debugging and tuning. <see cref="ApiBrain"/> writes one line per exchange:
/// the messages added since the previous line of that conversation (never the system text or
/// the tools, which are the same every time), the response content, usage and cost, and a
/// <c>thinking</c> list with the text of the response's thinking blocks (the summaries the API
/// returns with <c>display: "summarized"</c>, see <see cref="ThinkingSummaries"/>). A failure
/// to write is logged and never thrown, so a full disk can't stop the bot. Safe to call from
/// any thread.
/// </summary>
public sealed class Transcript
{
    private readonly object _gate = new();
    private readonly string _dir;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _clock;
    private bool _failed;

    /// <param name="dir">The config folder; lines go to its <c>transcripts</c> subfolder.</param>
    /// <param name="log">Where write failures are reported (once until a write succeeds again).</param>
    /// <param name="clock">Time source for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
    public Transcript(string dir, Action<string>? log = null, Func<DateTime>? clock = null)
    {
        _dir = Path.Combine(dir, "transcripts");
        _log = log ?? (_ => { });
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>The file today's lines go to.</summary>
    public string CurrentPath => Path.Combine(_dir, $"{_clock():yyyy-MM-dd}.jsonl");

    /// <summary>The non-empty text of each thinking block in <paramref name="content"/>, in order:
    /// the model's own summary of why it made its move. Empty when thinking was omitted (an
    /// empty string with only a signature) or there was none. The blocks themselves aren't touched;
    /// they go back into the conversation exactly as received.</summary>
    public static IReadOnlyList<string> ThinkingSummaries(JsonArray content) =>
        content.OfType<JsonObject>()
            .Where(b => b["type"] is JsonValue t && t.TryGetValue<string>(out var type) && type == "thinking")
            .Select(b => b["thinking"] is JsonValue v && v.TryGetValue<string>(out var text) ? text.Trim() : "")
            .Where(text => text.Length > 0)
            .ToArray();

    /// <summary>Append one line. An "at" timestamp (UTC, ISO 8601) goes first when the entry
    /// has none. The entry isn't changed or kept.</summary>
    public void Append(JsonObject entry)
    {
        try
        {
            var now = _clock();
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                if (!entry.ContainsKey("at"))
                    writer.WriteString("at", now.ToString("O"));
                foreach (var (key, value) in entry)
                {
                    writer.WritePropertyName(key);
                    if (value is null) writer.WriteNullValue();
                    else value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            buffer.WriteByte((byte)'\n');

            lock (_gate)
            {
                Directory.CreateDirectory(_dir);
                using (var file = new FileStream(Path.Combine(_dir, $"{now:yyyy-MM-dd}.jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
                    buffer.WriteTo(file);
                _failed = false;
            }
        }
        catch (Exception ex)
        {
            bool first;
            lock (_gate)
            {
                first = !_failed;
                _failed = true;
            }
            if (first)
                _log($"brain: couldn't write the transcript ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
