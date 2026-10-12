using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// The notes of one fight played by the API brain: a throwaway copy of what the brain's notes
/// tools read in the game, so it can open the wiki exactly as it does there, and nothing it
/// writes reaches the repo.
/// <list type="bullet">
/// <item>Copied from the checkout: <c>strategy/learned.md</c> and everything under
/// <c>strategy/wiki/</c>. The system prompt's wiki index and learned.md are read from the copy,
/// which holds the same bytes, so the prompt is the one the game builds from the checkout.</item>
/// <item>Not copied: <c>runs/</c> (the journal of Mark's real run says nothing about this fight,
/// so the brain starts with no journal, as after taking over a run that has none) and
/// <c>strategy/pending/</c> (lessons held for review; the notes tools never show them to the
/// model, and an empty folder means every line found there afterwards was written in this fight).</item>
/// <item>Afterwards <see cref="Written"/> lists every file that is new or changed: the journal,
/// the held lessons, a page the brain started or rewrote. That list goes into the result line.</item>
/// </list>
/// </summary>
public sealed class ApiNotes
{
    /// <summary>The most text of one written file the result line keeps.</summary>
    public const int MostText = 6000;

    private readonly Dictionary<string, string> _before = new(StringComparer.Ordinal);

    private ApiNotes(string root) => Root = root;

    /// <summary>The copy's folder: the notes root the brain's <see cref="NotesTools"/> get.</summary>
    public string Root { get; }

    /// <summary>How many files were copied.</summary>
    public int Copied => _before.Count;

    /// <summary>Copies the notes of <paramref name="repo"/> into the new folder <paramref name="root"/>.</summary>
    public static ApiNotes CopyFrom(string repo, string root)
    {
        var notes = new ApiNotes(Path.GetFullPath(root));
        Directory.CreateDirectory(notes.Root);
        var learned = Path.Combine(repo, NotesTools.Learned.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(learned))
            notes.Copy(learned, NotesTools.Learned);
        var wiki = Path.Combine(repo, NotesTools.WikiFolder.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(wiki))
            foreach (var path in Directory.EnumerateFiles(wiki, "*", SearchOption.AllDirectories))
                notes.Copy(path, NotesTools.WikiFolder + "/" + Path.GetRelativePath(wiki, path).Replace(Path.DirectorySeparatorChar, '/'));
        return notes;
    }

    private void Copy(string source, string file)
    {
        var target = Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        _before[file] = Hash(File.ReadAllBytes(target));
    }

    /// <summary>
    /// Every file of the copy that is new, changed or gone since it was made, by its path under
    /// the notes root, sorted: <c>{file, change, kind, bytes, text}</c>. <c>change</c> is "new",
    /// "changed" or "removed"; <c>kind</c> says what the file is ("journal", "held lesson",
    /// "journal archive", "learned", "wiki page", "other"); <c>text</c> is its content now, cut at
    /// <see cref="MostText"/> characters. Never throws: a file that can't be read is listed without text.
    /// </summary>
    public JsonArray Written()
    {
        var written = new JsonArray();
        var now = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                now[Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/')] = path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return written;
        }

        foreach (var (file, path) in now.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            // A temporary file of an atomic write that was cut short is no note.
            if (file.EndsWith(".tmp", StringComparison.Ordinal))
                continue;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                written.Add(new JsonObject { ["file"] = file, ["change"] = "unreadable", ["kind"] = KindOf(file) });
                continue;
            }
            var known = _before.TryGetValue(file, out var before);
            if (known && before == Hash(bytes))
                continue;
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            var entry = new JsonObject
            {
                ["file"] = file,
                ["change"] = known ? "changed" : "new",
                ["kind"] = KindOf(file),
                ["bytes"] = bytes.Length,
                ["text"] = text.Length <= MostText ? text : text[..MostText] + "\n...(cut)",
            };
            written.Add(entry);
        }
        foreach (var file in _before.Keys.Where(file => !now.ContainsKey(file)).OrderBy(file => file, StringComparer.Ordinal))
            written.Add(new JsonObject { ["file"] = file, ["change"] = "removed", ["kind"] = KindOf(file) });
        return written;
    }

    /// <summary>What a notes file is, by its path under the notes root.</summary>
    public static string KindOf(string file) =>
        file == NotesTools.Journal ? "journal"
        : file.StartsWith(NotesTools.PendingFolder + "/", StringComparison.Ordinal) ? "held lesson"
        : file.StartsWith("runs/", StringComparison.Ordinal) ? "journal archive"
        : file == NotesTools.Learned ? "learned"
        : NotesTools.IsWikiPage(file) ? "wiki page"
        : "other";

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
