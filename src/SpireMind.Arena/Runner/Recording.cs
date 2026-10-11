using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>One action a recorded session really ran: its id as the tools ran it, and the label
/// the tools reported for it (act_sequence reports one, act does not).</summary>
internal readonly record struct RanAction(string Id, string? Label);

/// <summary>
/// One tool call of a recorded session with what came back. For act and act_sequence,
/// <see cref="Ran"/> is what really ran (a sequence can stop early, and it finds later cards
/// again by label, so the ids that ran are not always the ids that were sent), and
/// <see cref="Snapshot"/> is the snapshot the result carried: "next" after a run, "current"
/// after a refusal, the result itself for wait_for_decision and get_state.
/// </summary>
internal sealed record RecordedCall
{
    /// <summary>The transcript line of the tool result (1-based).</summary>
    public required int Line { get; init; }

    /// <summary>The tool's short name: act, act_sequence, wait_for_decision, get_state, say, get_deck, get_persona.</summary>
    public required string Tool { get; init; }

    /// <summary>The ids the model sent.</summary>
    public IReadOnlyList<string> Sent { get; init; } = [];
    public IReadOnlyList<RanAction> Ran { get; init; } = [];
    public JsonObject? Snapshot { get; init; }

    /// <summary>The tools refused the whole call (a stale version, an id that is not offered): nothing ran.</summary>
    public string? Error { get; init; }

    /// <summary>Why a sequence stopped early.</summary>
    public string? Stopped { get; init; }
    public IReadOnlyList<string> NotRun { get; init; } = [];

    /// <summary>False when the transcript has the call but no result for it.</summary>
    public bool Answered { get; init; } = true;
}

/// <summary>
/// A recorded fight: the stream-json transcript a batch saved for it
/// (<c>transcripts/&lt;scenario&gt;__&lt;model&gt;.jsonl</c>), read into the session's tool
/// calls in the order their results came back, with the fight's line of results.jsonl beside it.
/// A position test replays it (<see cref="PositionReplay"/>).
/// <para>
/// What ran is taken from each tool result, never from what the model sent: <c>done</c> is one
/// id for act and a list of id and label for act_sequence; a refused call ran nothing. A tool
/// result's text is a string or a list of text blocks, and the snapshot it carries is under
/// <c>next</c> (after a run), <c>current</c> (after a refusal), or is the result itself.
/// </para>
/// </summary>
internal sealed class Recording
{
    public required string ScenarioId { get; init; }
    public required string Model { get; init; }

    /// <summary>The transcript file, or "" for a recording that was made in memory (the checks).</summary>
    public required string TranscriptPath { get; init; }

    /// <summary>The folder the results.jsonl lies in, or "" for a recording made in memory.</summary>
    public string Folder { get; init; } = "";

    /// <summary>The fight's line of results.jsonl: the last one that counts (won, lost,
    /// abandoned), else the last there is. Null when the file holds none for it.</summary>
    public FightResult? Row { get; init; }

    public List<RecordedCall> Calls { get; } = [];

    /// <summary>Sessions the transcript holds (1 plus the resumes).</summary>
    public int Sessions { get; private set; } = 1;

    /// <summary>Lines of the transcript that were not JSON.</summary>
    public int UnreadableLines { get; private set; }

    /// <summary>The folder of a run, given as the folder or as its results.jsonl.</summary>
    public static string FolderOf(string path) =>
        Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";

    /// <summary>Where the batch that wrote <paramref name="folder"/> saved this fight's
    /// transcript: a whole fight played without a switch (<see cref="RunCommand.TranscriptName"/>).</summary>
    public static string TranscriptFile(string folder, string scenario, string model) =>
        Path.Combine(folder, RunCommand.TranscriptFolder, RunCommand.TranscriptName(scenario, model, ExperimentLabel.Base));

    /// <summary>Reads the recording of <paramref name="model"/>'s fight on
    /// <paramref name="scenario"/> from a run's folder (or its results.jsonl).</summary>
    /// <exception cref="FileNotFoundException">The folder has no such transcript.</exception>
    public static Recording Load(string resultsPath, string scenario, string model)
    {
        var folder = FolderOf(resultsPath);
        var path = TranscriptFile(folder, scenario, model);
        if (!File.Exists(path))
            throw new FileNotFoundException($"There is no transcript at {path}.");
        var results = Path.Combine(folder, RunCommand.ResultsFile);
        // The whole fight's own line: never a position run's, and the plain batch's, whose transcript this is.
        var rows = File.Exists(results)
            ? FightResult.ReadFile(results, out _)
                .Where(r => r.ScenarioId == scenario && r.Model == model && r.Position is null && r.Experiment == ExperimentLabel.Base).ToList()
            : [];
        var recording = new Recording
        {
            ScenarioId = scenario, Model = model, TranscriptPath = path, Folder = folder,
            Row = rows.LastOrDefault(r => r.Counts) ?? rows.LastOrDefault(),
        };
        recording.Read(File.ReadLines(path));
        return recording;
    }

    /// <summary>A recording from transcript lines that are already in memory.</summary>
    public static Recording FromLines(string scenario, string model, IEnumerable<string> lines)
    {
        var recording = new Recording { ScenarioId = scenario, Model = model, TranscriptPath = "" };
        recording.Read(lines);
        return recording;
    }

    private sealed record Use(string Tool, JsonObject Input);

    private void Read(IEnumerable<string> lines)
    {
        var uses = new Dictionary<string, Use>(StringComparer.Ordinal);
        var order = new List<string>();
        var answered = new HashSet<string>(StringComparer.Ordinal);
        var n = 0;
        foreach (var line in lines)
        {
            n++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? o;
            try { o = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { UnreadableLines++; continue; }
            if (o is null) { UnreadableLines++; continue; }

            switch (Str(o, "type"))
            {
                case "arena":
                    if ((Str(o, "note") ?? "").Contains("resume", StringComparison.OrdinalIgnoreCase)) Sessions++;
                    break;
                case "assistant":
                    foreach (var block in Blocks(o))
                    {
                        if (Str(block, "type") != "tool_use" || Str(block, "id") is not { } id || uses.ContainsKey(id)) continue;
                        var name = Str(block, "name") ?? "";
                        var at = name.LastIndexOf("__", StringComparison.Ordinal);
                        uses[id] = new Use(at >= 0 ? name[(at + 2)..] : name, block["input"] as JsonObject ?? new JsonObject());
                        order.Add(id);
                    }
                    break;
                case "user":
                    foreach (var block in Blocks(o))
                    {
                        if (Str(block, "type") != "tool_result" || Str(block, "tool_use_id") is not { } id) continue;
                        if (!uses.TryGetValue(id, out var use) || !answered.Add(id)) continue;
                        Calls.Add(Call(n, use, ResultText(block["content"])));
                    }
                    break;
            }
        }

        // A call the session never got an answer to (it was stopped in the middle): kept, so the replay can say so.
        foreach (var id in order)
            if (!answered.Contains(id))
                Calls.Add(new RecordedCall { Line = n, Tool = uses[id].Tool, Sent = SentIds(uses[id]), Answered = false });
    }

    private static IEnumerable<JsonObject> Blocks(JsonObject line)
    {
        if (line["message"] is not JsonObject message || message["content"] is not JsonArray content) yield break;
        foreach (var block in content)
            if (block is JsonObject b) yield return b;
    }

    /// <summary>A tool result's text: the content is a string (errors) or a list of text blocks.</summary>
    private static string ResultText(JsonNode? content)
    {
        if (content is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        if (content is not JsonArray parts) return "";
        var text = new StringBuilder();
        foreach (var part in parts)
            if (part is JsonObject p && Str(p, "type") == "text") text.Append(Str(p, "text"));
        return text.ToString();
    }

    private static List<string> SentIds(Use use)
    {
        if (use.Tool == "act") return Str(use.Input, "action_id") is { } id ? [id] : [];
        if (use.Tool == "act_sequence" && use.Input["actions"] is JsonArray actions)
            return actions.Select(a => a is JsonValue v && v.TryGetValue<string>(out var id) ? id : "").Where(id => id.Length > 0).ToList();
        return [];
    }

    private static RecordedCall Call(int line, Use use, string resultText)
    {
        JsonObject? payload = null;
        try { payload = JsonNode.Parse(resultText) as JsonObject; }
        catch (JsonException) { /* a tool result that is not JSON: kept as an error text below */ }

        var sent = SentIds(use);
        if (payload is null)
            return new RecordedCall { Line = line, Tool = use.Tool, Sent = sent, Error = resultText.Length > 0 ? resultText : "the result was empty" };

        switch (use.Tool)
        {
            case "wait_for_decision" or "get_state":
                return new RecordedCall { Line = line, Tool = use.Tool, Snapshot = payload["screen"] is not null ? payload : null, Error = Str(payload, "error") };
            case "act" or "act_sequence":
            {
                var ran = new List<RanAction>();
                if (payload["done"] is JsonValue one && one.TryGetValue<string>(out var id)) ran.Add(new RanAction(id, null));
                else if (payload["done"] is JsonArray many)
                    foreach (var item in many)
                        if (item is JsonObject d && Str(d, "id") is { } ranId) ran.Add(new RanAction(ranId, Str(d, "label")));
                var notRun = payload["not_run"] is JsonArray rest
                    ? rest.Select(r => r is JsonValue rv && rv.TryGetValue<string>(out var rid) ? rid : "").Where(r => r.Length > 0).ToList()
                    : [];
                return new RecordedCall
                {
                    Line = line, Tool = use.Tool, Sent = sent, Ran = ran,
                    Snapshot = (payload["next"] ?? payload["current"]) as JsonObject,
                    Error = Str(payload, "error"), Stopped = Str(payload, "stopped"), NotRun = notRun,
                };
            }
            default:
                return new RecordedCall { Line = line, Tool = use.Tool, Error = Str(payload, "error") };
        }
    }

    internal static string? Str(JsonObject o, string key) => FightResult.Str(o[key]);
}
