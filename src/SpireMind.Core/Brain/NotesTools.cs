using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpireMind.Core.Brain;

/// <summary>
/// The brain's only file access: <c>notes_read</c>, <c>notes_write</c> and <c>notes_append</c>
/// for the run journal (<c>runs/current.md</c>) and the lessons file (<c>strategy/learned.md</c>)
/// under one root folder. Anything else is refused, traversal included. Claude Code has its
/// own file tools, so these are for the API brain only. Writes are atomic (a temporary file
/// moved over the target) and create the folders they need. Safe to call from any thread.
/// </summary>
public sealed partial class NotesTools
{
    public const string Journal = "runs/current.md";
    public const string Learned = "strategy/learned.md";

    /// <summary>The files the tools accept, exactly as written here.</summary>
    public static IReadOnlyList<string> Files { get; } = [Journal, Learned];

    /// <summary>The largest a notes file may get, in UTF-8 bytes, through notes_write or
    /// notes_append. Small enough that the whole file fits in one reply with room to think, even
    /// at the smallest max_tokens the brain sends by default (16000, see ApiBrain.MaxTokensFor),
    /// so it can always be rewritten.</summary>
    public const int MaxBytes = 32 * 1024;

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;

    /// <param name="root">The folder the two files live under (the repo, or the config folder
    /// when the repo is missing).</param>
    /// <param name="clock">Time source for archive names; defaults to <see cref="DateTime.UtcNow"/>.</param>
    public NotesTools(string root, Func<DateTime>? clock = null)
    {
        Root = Path.GetFullPath(root);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public string Root { get; }

    public IReadOnlyList<ToolSpec> Specs { get; } =
    [
        new("notes_read",
            "Read one of your notes files: runs/current.md (this run's journal: header line, Build plan, Orders, floor log) " +
            "or strategy/learned.md (lessons from past runs). A missing file comes back empty with exists=false.",
            Schema(new JsonObject { ["file"] = FileProperty() }, ["file"])),
        new("notes_write",
            "Replace one of your notes files with new content: runs/current.md or strategy/learned.md. Send the whole file, " +
            "not a diff; read it first if you only want to change part of it. Use notes_append to add a line instead. " +
            $"A file can be at most {MaxBytes / 1024} KB.",
            Schema(new JsonObject
            {
                ["file"] = FileProperty(),
                ["content"] = new JsonObject { ["type"] = "string", ["description"] = "The complete new file content (Markdown)" },
            }, ["file", "content"])),
        new("notes_append",
            "Add text to the end of one of your notes files, such as the floor's line in the runs/current.md log, without " +
            "sending the whole file. It goes on a new line; a missing file is created.",
            Schema(new JsonObject
            {
                ["file"] = FileProperty(),
                ["content"] = new JsonObject { ["type"] = "string", ["description"] = "The text to add (Markdown), usually one line" },
            }, ["file", "content"])),
    ];

    /// <summary>True for the tool names this class handles.</summary>
    public bool Handles(string name) => name is "notes_read" or "notes_write" or "notes_append";

    /// <summary>Run notes_read, notes_write or notes_append. Bad paths and IO failures come back as error outcomes.</summary>
    public Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var file = args["file"] is JsonValue f && f.TryGetValue<string>(out var s) ? s : null;
        if (file is null || !Files.Contains(file))
            return Task.FromResult(Error($"file must be one of: {string.Join(", ", Files)} (got {Describe(file)}). No other files are available."));

        try
        {
            return Task.FromResult(name switch
            {
                "notes_read" => Read(file),
                "notes_write" => Write(file, Content(args)),
                "notes_append" => Append(file, Content(args)),
                _ => Error($"Unknown tool '{name}'"),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Error($"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    /// <summary>The content of an allowed file, or "" when it doesn't exist or can't be read.
    /// For the brain's own use (the run journal in a fresh context).</summary>
    public string ReadText(string file)
    {
        if (!Files.Contains(file))
            throw new ArgumentException($"Not a notes file: {file}", nameof(file));
        try
        {
            lock (_gate)
            {
                var path = PathOf(file);
                return File.Exists(path) ? File.ReadAllText(path) : "";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private ToolOutcome Read(string file)
    {
        lock (_gate)
        {
            var path = PathOf(file);
            var exists = File.Exists(path);
            return new ToolOutcome(new JsonObject
            {
                ["file"] = file,
                ["exists"] = exists,
                ["content"] = exists ? File.ReadAllText(path) : "",
            });
        }
    }

    private ToolOutcome Write(string file, string? content)
    {
        if (content is null)
            return Error("content is required: the whole new file as a string.");
        var bytes = Utf8.GetByteCount(content);
        if (bytes > MaxBytes)
            return Error($"content is {bytes} bytes; the limit is {MaxBytes}. Shorten it (prune old log lines) and write again.");

        lock (_gate)
        {
            var path = PathOf(file);
            var reply = new JsonObject { ["file"] = file, ["written"] = true, ["bytes"] = bytes };
            if (file == Journal && ArchiveIfAnotherRun(path, content) is { } archived)
                reply["archived_previous_as"] = archived;
            ConfigFiles.WriteAtomic(path, content);
            return new ToolOutcome(reply);
        }
    }

    // Adds content on a line of its own, ending with a newline. Never archives: an appended
    // line belongs to the journal already there.
    private ToolOutcome Append(string file, string? content)
    {
        if (content is null || content.Trim().Length == 0)
            return Error("content is required: the text to add.");

        lock (_gate)
        {
            var path = PathOf(file);
            var old = File.Exists(path) ? File.ReadAllText(path) : "";
            var separator = old.Length > 0 && !old.EndsWith('\n') ? "\n" : "";
            var text = old + separator + content + (content.EndsWith('\n') ? "" : "\n");
            var bytes = Utf8.GetByteCount(text);
            if (bytes > MaxBytes)
                return Error($"{file} would be {bytes} bytes; the limit is {MaxBytes}. Rewrite it shorter with notes_write " +
                             "(prune old log lines), then append again.");
            ConfigFiles.WriteAtomic(path, text);
            return new ToolOutcome(new JsonObject { ["file"] = file, ["appended"] = true, ["bytes"] = bytes });
        }
    }

    // When the new journal starts with a different header line than the one on disk, or the old
    // one has a post-mortem and the new one doesn't, it is a new run: the old file is copied to
    // runs/<date>-<character>.md (from its header, as runs/README.md names archives) before it is
    // replaced, never overwriting an archive. The whole header line is compared, so most runs
    // differ there; two runs with the same header still differ in the post-mortem that ends the
    // finished one. A header edited mid-run costs a spare copy, never a lost journal. Returns the
    // archive's relative path, or null when nothing was archived. Caller holds _gate.
    private string? ArchiveIfAnotherRun(string path, string newContent)
    {
        if (!File.Exists(path))
            return null;
        var old = File.ReadAllText(path);
        if (old.Trim().Length == 0)
            return null;
        var finished = PostMortem().IsMatch(old) && !PostMortem().IsMatch(newContent);
        if (HeaderLine(old) == HeaderLine(newContent) && !finished)
            return null;

        var stem = RunKey(HeaderLine(old)) is { } run ? $"{run.Date}-{run.Character}" : $"{_clock():yyyy-MM-dd}-previous";
        var dir = Path.GetDirectoryName(path)!;
        var name = $"{stem}.md";
        for (var n = 2; File.Exists(Path.Combine(dir, name)); n++)
            name = $"{stem}-{n}.md";
        File.Copy(path, Path.Combine(dir, name));
        return $"runs/{name}";
    }

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static string? Content(JsonObject args) => args["content"] is JsonValue c && c.TryGetValue<string>(out var text) ? text : null;

    private static string HeaderLine(string content) => content.TrimStart().Split('\n', 2)[0].Trim();

    // The character and start date in a header line such as "# Ironclad, started 2026-10-10, Neow: ...".
    // Null when the line doesn't look like that.
    private static (string Character, string Date)? RunKey(string header)
    {
        var match = Header().Match(header);
        return match.Success ? (match.Groups["c"].Value.ToLowerInvariant(), match.Groups["d"].Value) : null;
    }

    private string PathOf(string file) => Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar));

    private static string Describe(string? file) => file is null ? "nothing" : $"'{file}'";

    private static ToolOutcome Error(string message) => new(new JsonObject { ["error"] = message }, IsError: true);

    private static JsonObject FileProperty() => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(Files.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()),
        ["description"] = "Which notes file",
    };

    private static JsonObject Schema(JsonObject properties, string[] required) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
    };

    [GeneratedRegex(@"^#+\s*(?:the\s+)?(?<c>[A-Za-z]+)\s*,\s*started\s+(?<d>\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Header();

    [GeneratedRegex(@"post[- ]?mortem", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PostMortem();
}
