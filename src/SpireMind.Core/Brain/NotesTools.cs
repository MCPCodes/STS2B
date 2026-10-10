using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpireMind.Core.Brain;

/// <summary>
/// The brain's only file access: <c>notes_read</c>, <c>notes_write</c> and <c>notes_append</c>
/// for the run journal (<c>runs/current.md</c>), the lessons file (<c>strategy/learned.md</c>)
/// and the wiki's pages (see <see cref="IsWikiPage"/>) under one root folder, and
/// <c>notes_list</c>, which lists the pages. Anything else is refused, traversal included, and a
/// rewrite of a page must keep its verified facts (see <see cref="KeepsFacts"/>).
/// Claude Code has its own file tools, so these are for the API brain only. Writes are atomic
/// (a temporary file moved over the target) and create the folders they need. Safe to call
/// from any thread.
/// </summary>
public sealed partial class NotesTools
{
    public const string Journal = "runs/current.md";
    public const string Learned = "strategy/learned.md";

    /// <summary>The wiki's folder, which holds its pages and the conventions in README.md.</summary>
    public const string WikiFolder = "strategy/wiki";

    /// <summary>The fixed files the tools accept besides wiki pages, exactly as written here.</summary>
    public static IReadOnlyList<string> Files { get; } = [Journal, Learned];

    /// <summary>The forms a file can take, for the schema and for errors.</summary>
    public const string FileForms =
        "runs/current.md, strategy/learned.md, or a wiki page: strategy/wiki/<slug>.md or strategy/wiki/<folder>/<slug>.md, " +
        "where folder and slug are lowercase letters and digits with single hyphens between words (strategy/wiki/enemies/jaw-worm.md); " +
        "readme and Windows device names (con, prn, aux, nul, com1 to com9, lpt1 to lpt9) are reserved";

    /// <summary>The largest a notes file may get, in UTF-8 bytes, through notes_write or
    /// notes_append. Small enough that the whole file fits in one reply with room to think, even
    /// at the smallest max_tokens the brain sends by default (16000, see ApiBrain.MaxTokensFor),
    /// so it can always be rewritten.</summary>
    public const int MaxBytes = 32 * 1024;

    // The longest summary notes_list and the prompt's wiki index show; a longer line is cut.
    private const int MaxSummaryChars = 200;

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;

    /// <param name="root">The folder the notes files live under (the repo, or the config folder
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
            "Read one of your notes files: runs/current.md (this run's journal: header line, Build plan, Orders, floor log), " +
            "strategy/learned.md (short rules from past runs) or a wiki page under strategy/wiki/ (notes_list lists them). " +
            "A missing file comes back empty with exists=false.",
            Schema(new JsonObject { ["file"] = FileProperty() }, ["file"])),
        new("notes_write",
            "Replace one of your notes files with new content: runs/current.md, strategy/learned.md or a wiki page (a new " +
            "page is created, with its folder). Send the whole file, not a diff; read it first if you only want to change " +
            "part of it. On a wiki page with a Facts section, everything above its ## Lessons heading must stay exactly as it " +
            $"is. Use notes_append to add a line instead. A file can be at most {MaxBytes / 1024} KB.",
            Schema(new JsonObject
            {
                ["file"] = FileProperty(),
                ["content"] = new JsonObject { ["type"] = "string", ["description"] = "The complete new file content (Markdown)" },
            }, ["file", "content"])),
        new("notes_append",
            "Add text to the end of one of your notes files without sending the whole file, such as the floor's line in the " +
            "runs/current.md log or a dated line in a wiki page's Lessons, its last section. It goes on a new line; a missing " +
            "file is created.",
            Schema(new JsonObject
            {
                ["file"] = FileProperty(),
                ["content"] = new JsonObject { ["type"] = "string", ["description"] = "The text to add (Markdown), usually one line" },
            }, ["file", "content"])),
        new("notes_list",
            "List the wiki pages under strategy/wiki/ as they are now, sorted by path, each with its size, title and " +
            "one-line summary, and whether runs/current.md and strategy/learned.md exist.",
            Schema(new JsonObject(), [])),
    ];

    /// <summary>
    /// True for a wiki page path: <c>strategy/wiki/&lt;slug&gt;.md</c> or
    /// <c>strategy/wiki/&lt;folder&gt;/&lt;slug&gt;.md</c>, folder and slug made of lowercase
    /// letters and digits with single hyphens between words. Nothing deeper, no dots or
    /// backslashes, so no traversal, and no capitals, so README.md is never a page. A few names
    /// are refused as folder or slug (see <see cref="ReservedNames"/>).
    /// </summary>
    public static bool IsWikiPage(string? file) =>
        file is not null && WikiPath().Match(file) is { Success: true } match
        && !ReservedNames.Contains(match.Groups["slug"].Value)
        && !(match.Groups["folder"].Success && ReservedNames.Contains(match.Groups["folder"].Value));

    // readme, since on a case-insensitive disk (macOS) it would be README.md, and the names
    // Windows reads as devices whatever their extension (nul.md is the null device there).
    private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
    {
        "readme", "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>True for every file the tools accept: the fixed files and wiki pages.</summary>
    public static bool IsNotesFile(string? file) => file is not null && (Files.Contains(file) || IsWikiPage(file));

    /// <summary>True for the tool names this class handles.</summary>
    public bool Handles(string name) => name is "notes_read" or "notes_write" or "notes_append" or "notes_list";

    /// <summary>Run notes_read, notes_write, notes_append or notes_list. Bad paths and IO failures come back as error outcomes.</summary>
    public Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (name == "notes_list")
            return Task.FromResult(List());

        var file = args["file"] is JsonValue f && f.TryGetValue<string>(out var s) ? s : null;
        if (!IsNotesFile(file))
            return Task.FromResult(Error($"file must be one of: {FileForms} (got {Describe(file)}). No other files are available."));

        try
        {
            return Task.FromResult(name switch
            {
                "notes_read" => Read(file!),
                "notes_write" => Write(file!, Content(args)),
                "notes_append" => Append(file!, Content(args)),
                _ => Error($"Unknown tool '{name}'"),
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(Error($"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    /// <summary>The content of a file the tools accept, or "" when it doesn't exist or can't be
    /// read. For the brain's own use (the run journal in a fresh context).</summary>
    public string ReadText(string file)
    {
        if (!IsNotesFile(file))
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
            return Error($"content is {bytes} bytes; the limit is {MaxBytes}. Shorten it (prune old log lines or lessons) and write again.");

        lock (_gate)
        {
            var path = PathOf(file);
            if (IsWikiPage(file) && File.Exists(path) && !KeepsFacts(File.ReadAllText(path), content))
                return Error($"{file} has a Facts section, verified from the game files, so a rewrite must keep everything above " +
                             "its ## Lessons heading exactly as it is and change only the Lessons. Read the page with notes_read and " +
                             "write it again, or add a line with notes_append.");
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
                             "(prune old log lines or lessons), then append again.");
            ConfigFiles.WriteAtomic(path, text);
            return new ToolOutcome(new JsonObject { ["file"] = file, ["appended"] = true, ["bytes"] = bytes });
        }
    }

    // The wiki pages, and the fixed files with whether they exist.
    private ToolOutcome List()
    {
        lock (_gate)
        {
            var files = new JsonArray();
            foreach (var file in Files)
            {
                var info = new FileInfo(PathOf(file));
                var entry = new JsonObject { ["file"] = file, ["exists"] = info.Exists };
                if (info.Exists)
                    entry["bytes"] = info.Length;
                files.Add(entry);
            }

            var pages = new JsonArray();
            foreach (var page in ListWiki(Root))
            {
                var entry = new JsonObject { ["file"] = page.File, ["bytes"] = page.Bytes };
                if (page.Title is not null)
                    entry["title"] = page.Title;
                if (page.Summary is not null)
                    entry["summary"] = page.Summary;
                pages.Add(entry);
            }
            return new ToolOutcome(new JsonObject { ["wiki"] = pages, ["files"] = files });
        }
    }

    /// <summary>
    /// The wiki pages under <paramref name="root"/> (files whose path <see cref="IsWikiPage"/>
    /// accepts, so not README.md), sorted by path. Each has its size, the title from its first
    /// line's heading, and its summary: the next non-empty line, unless that is a heading. A page
    /// that doesn't start with a heading has no title, and its first line is the summary. Blank
    /// lines before the heading don't count. Empty when the folder is missing; never throws.
    /// </summary>
    public static IReadOnlyList<WikiPage> ListWiki(string root)
    {
        var wiki = Path.Combine(root, WikiFolder.Replace('/', Path.DirectorySeparatorChar));
        var pages = new List<WikiPage>();
        var folders = new List<(string Dir, string Prefix)> { (wiki, WikiFolder + "/") };
        folders.AddRange(Entries(() => Directory.EnumerateDirectories(wiki)).Select(d => (d, $"{WikiFolder}/{Path.GetFileName(d)}/")));
        foreach (var (dir, prefix) in folders)
        {
            foreach (var path in Entries(() => Directory.EnumerateFiles(dir)))
            {
                var file = prefix + Path.GetFileName(path);
                if (IsWikiPage(file))
                    pages.Add(ReadPage(file, path));
            }
        }
        pages.Sort((a, b) => string.CompareOrdinal(a.File, b.File));
        return pages;
    }

    // A folder's entries, or none when it is missing or can't be read.
    private static List<string> Entries(Func<IEnumerable<string>> list)
    {
        try { return list().ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    // A page's size, title and summary; just its path when it can't be read.
    private static WikiPage ReadPage(string file, string path)
    {
        try
        {
            var bytes = new FileInfo(path).Length;
            string? title = null;
            string? summary = null;
            var first = true;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (first && line.StartsWith('#'))
                {
                    first = false;
                    title = line.TrimStart('#').Trim() is { Length: > 0 } name ? name : null;
                    continue;
                }
                if (!line.StartsWith('#'))
                    summary = line.Length > MaxSummaryChars ? line[..(MaxSummaryChars - 3)].TrimEnd() + "..." : line;
                break;
            }
            return new WikiPage(file, bytes, title, summary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WikiPage(file, 0, null, null);
        }
    }

    /// <summary>
    /// True when <paramref name="newPage"/> may replace the wiki page <paramref name="oldPage"/>:
    /// always when the old page has no <c>## Facts</c> heading, and otherwise only when both have
    /// the same text above their <c>## Lessons</c> heading (the whole old page when it has none),
    /// so the verified facts, and the name and summary above them, can't be changed by a rewrite.
    /// Line endings, trailing spaces and trailing blank lines don't count.
    /// </summary>
    public static bool KeepsFacts(string oldPage, string newPage) =>
        !FactsHeading().IsMatch(oldPage) || AboveLessons(oldPage) == AboveLessons(newPage);

    // A page's text above its first ## Lessons heading (all of it when it has none), each line
    // without trailing spaces, without the blank lines at its end.
    private static string AboveLessons(string page)
    {
        var lines = page.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        var lessons = lines.FindIndex(l => LessonsHeading().IsMatch(l));
        return string.Join("\n", lessons < 0 ? lines : lines.Take(lessons)).TrimEnd();
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

    // A plain string: wiki pages can't be an enum, and strict tool schemas don't take "pattern",
    // so the forms are in the description and CallAsync checks them.
    private static JsonObject FileProperty() => new()
    {
        ["type"] = "string",
        ["description"] = $"Which notes file: {FileForms}",
    };

    private static JsonObject Schema(JsonObject properties, string[] required)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0)
            schema["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        return schema;
    }

    [GeneratedRegex(@"^#+\s*(?:the\s+)?(?<c>[A-Za-z]+)\s*,\s*started\s+(?<d>\d{4}-\d{2}-\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Header();

    [GeneratedRegex(@"post[- ]?mortem", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PostMortem();

    [GeneratedRegex(@"^##[ \t]+Facts\b", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FactsHeading();

    [GeneratedRegex(@"\A##[ \t]+Lessons\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LessonsHeading();

    [GeneratedRegex(@"\Astrategy/wiki/(?:(?<folder>[a-z0-9]+(?:-[a-z0-9]+)*)/)?(?<slug>[a-z0-9]+(?:-[a-z0-9]+)*)\.md\z", RegexOptions.CultureInvariant)]
    private static partial Regex WikiPath();
}

/// <summary>A wiki page as notes_list and the system prompt's wiki index show it.</summary>
/// <param name="File">The path the notes tools take, such as <c>strategy/wiki/enemies/jaw-worm.md</c>.</param>
/// <param name="Bytes">Its size on disk.</param>
/// <param name="Title">Its first line's heading without the #, or null.</param>
/// <param name="Summary">The one-line summary after the heading, or null.</param>
public sealed record WikiPage(string File, long Bytes, string? Title, string? Summary);
