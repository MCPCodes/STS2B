using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
// The mod compiles Core with global usings for Godot and the game, and Godot has its own Environment and FileAccess.
// These aliases keep the .NET ones there.
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;

namespace SpireMind.Core.Brain;

/// <summary>
/// File helpers for the small JSON files in the config folder. Writes go to a temporary file
/// in the same folder and are then moved over the target, so a crash or a full disk never
/// leaves a half-written config.json or spend.json behind.
/// </summary>
internal static class ConfigFiles
{
    /// <summary>Lenient reading: config.json is edited by hand, so comments and trailing commas are fine.</summary>
    internal static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/> atomically, creating the folder when needed.</summary>
    internal static void WriteAtomic(string path, string text)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }

    /// <summary>Writes a JSON object atomically, indented, with a trailing newline.</summary>
    internal static void WriteJson(string path, JsonObject json) =>
        WriteAtomic(path, json.ToJsonString(WriteOptions) + "\n");

    /// <summary>Moves a file that couldn't be read aside to <c>&lt;path&gt;.bak</c> (replacing an older
    /// backup) so it can be inspected, and returns the backup's path. Null when the move failed.</summary>
    internal static string? MoveAside(string path)
    {
        var backup = path + ".bak";
        try
        {
            File.Move(path, backup, overwrite: true);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Replaces a leading "~" with the home folder, since config.json is written by hand.</summary>
    internal static string ExpandHome(string path)
    {
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        return path;
    }
}
