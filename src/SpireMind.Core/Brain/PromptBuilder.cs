using System.Text;
using System.Text.RegularExpressions;

namespace SpireMind.Core.Brain;

/// <summary>The brain's system text and the files it was built from (full paths, in order).</summary>
public sealed record SystemPrompt(string Text, IReadOnlyList<string> Files);

/// <summary>
/// Builds the brain's system text from the same files Claude Code's <c>/play</c> uses, so both
/// brains behave alike: the play prompt, then the strategy notes the prompt tells the model to
/// read and the boss and elite notes general.md points to (included here, since the brain has
/// no file access), then a short appendix about this harness. Missing files are skipped. See
/// docs/brain.md.
/// </summary>
public static partial class PromptBuilder
{
    public const string PlayPrompt = ".claude/commands/play.md";
    public const string GeneralNotes = "strategy/general.md";
    public const string BossNotes = "strategy/bosses-and-elites.md";
    public const string LearnedNotes = "strategy/learned.md";
    public const string CoopNotes = "strategy/coop.md";

    /// <summary>What <c>$ARGUMENTS</c> in the play prompt becomes: the brain always plays on.</summary>
    public const string Scope = "the whole run";

    /// <summary>The appendix for this harness, after the notes.</summary>
    public const string HarnessNotes =
        "## Playing from inside the game\n\n" +
        "You run inside the game itself, through the SpireMind mod, not in Claude Code. Only tool calls do anything: " +
        "text you write outside `say` is seen by nobody, so keep it short.\n\n" +
        "- The strategy notes the prompt asks you to read are already included above under Strategy notes. Don't try to read strategy files.\n" +
        "- `runs/current.md` and `strategy/learned.md` are the only files you can use: read them with `notes_read`, add a line " +
        "with `notes_append` (use it for each floor's log line) and replace them with `notes_write` (the whole file, for the " +
        "Build plan, Orders or the post-mortem). There is no other file access. Archiving is automatic: when you write " +
        "`runs/current.md` with a new header line, or without the post-mortem the old journal has (a new run), the old journal " +
        "is kept as `runs/<date>-<character>.md` for you, so keep the header line unchanged for the rest of a run.\n" +
        "- When the conversation gets long, it is restarted with your run journal, the recent chat and the current state. " +
        "Keep the Build plan, Orders and floor log in `runs/current.md` current so nothing is lost.\n" +
        "- When `act_sequence` is in your tools, it can play a whole turn from one snapshot; see its description.\n" +
        "- Waiting costs nothing here: `wait_for_decision` waits as long as it takes, through enemy turns, teammates and pauses. " +
        "Never end your turn while a run is in progress; call `wait_for_decision` instead.";

    /// <summary>
    /// Build the system text from <paramref name="folder"/> (the repo, or a fallback folder with
    /// the same layout).
    /// </summary>
    /// <param name="character">The character being played ("Ironclad", "The Silent"), or null
    /// when unknown; picks <c>strategy/characters/&lt;name&gt;.md</c>.</param>
    /// <param name="multiplayer">Adds <c>strategy/coop.md</c>.</param>
    /// <param name="notesFolder">Where the brain writes its notes, when that isn't
    /// <paramref name="folder"/>. <c>strategy/learned.md</c> is read from there when it exists
    /// there, so lessons written in fallback mode reach the next prompt.</param>
    public static SystemPrompt Build(string folder, string? character, bool multiplayer, string? notesFolder = null)
    {
        var files = new List<string>();
        var text = new StringBuilder();

        if (TryRead(Path.Combine(folder, PlayPrompt)) is { } play)
        {
            files.Add(Path.GetFullPath(Path.Combine(folder, PlayPrompt)));
            text.Append(StripFrontmatter(play).Replace("$ARGUMENTS", Scope).Trim()).Append("\n\n");
        }

        var notes = new List<(string Name, string Path)>
        {
            (GeneralNotes, Path.Combine(folder, GeneralNotes)),
            (BossNotes, Path.Combine(folder, BossNotes)),
        };
        if (CharacterFile(character) is { } characterFile)
            notes.Add((characterFile, Path.Combine(folder, characterFile)));
        var learned = notesFolder is not null && File.Exists(Path.Combine(notesFolder, LearnedNotes))
            ? Path.Combine(notesFolder, LearnedNotes)
            : Path.Combine(folder, LearnedNotes);
        notes.Add((LearnedNotes, learned));
        if (multiplayer)
            notes.Add((CoopNotes, Path.Combine(folder, CoopNotes)));

        var section = new StringBuilder();
        foreach (var (name, path) in notes)
        {
            if (TryRead(path) is not { } content || content.Trim().Length == 0)
                continue;
            files.Add(Path.GetFullPath(path));
            section.Append($"<file path=\"{name}\">\n").Append(content.Trim()).Append("\n</file>\n\n");
        }
        if (section.Length > 0)
            text.Append("## Strategy notes\n\n").Append(section);

        text.Append(HarnessNotes).Append('\n');
        return new SystemPrompt(text.ToString(), files);
    }

    /// <summary>The play prompt without its leading YAML frontmatter (a block between two
    /// <c>---</c> lines at the very start). Text without frontmatter comes back unchanged.</summary>
    public static string StripFrontmatter(string text)
    {
        var match = Frontmatter().Match(text);
        return match.Success ? text[match.Length..] : text;
    }

    /// <summary>The notes file for a character, such as <c>strategy/characters/ironclad.md</c>
    /// for "The Ironclad". Null when the name is unknown or isn't a plain word.</summary>
    public static string? CharacterFile(string? character)
    {
        var name = character?.Trim() ?? "";
        if (name.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
            name = name[4..].TrimStart();
        name = name.ToLowerInvariant();
        return PlainName().IsMatch(name) ? $"strategy/characters/{name}.md" : null;
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\A﻿?---[ \t]*\r?\n.*?^---[ \t]*(?:\r?\n|\z)", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex Frontmatter();

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9_-]{0,39}\z")]
    private static partial Regex PlainName();
}
