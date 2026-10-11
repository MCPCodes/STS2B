using System.Text;
using System.Text.RegularExpressions;

namespace SpireMind.Core.Brain;

/// <summary>The brain's system text and the files it was built from (full paths, in order).</summary>
public sealed record SystemPrompt(string Text, IReadOnlyList<string> Files);

/// <summary>
/// Builds the brain's system text from the same files Claude Code's <c>/play</c> uses, so both
/// brains behave alike: the play prompt, then the strategy notes the prompt tells the model to
/// read and the boss and elite notes general.md points to (included here, since the brain has
/// no file access beyond the notes tools), then an index of the wiki's pages, then a short
/// appendix about this harness. Missing files are skipped. See docs/brain.md.
/// </summary>
public static partial class PromptBuilder
{
    public const string PlayPrompt = ".claude/commands/play.md";
    public const string GeneralNotes = "strategy/general.md";
    public const string RulesNotes = "strategy/rules.md";
    public const string BossNotes = "strategy/bosses-and-elites.md";
    public const string LearnedNotes = "strategy/learned.md";
    public const string CoopNotes = "strategy/coop.md";

    /// <summary>What <c>$ARGUMENTS</c> in the play prompt becomes: the brain always plays on.</summary>
    public const string Scope = "the whole run";

    /// <summary>What <see cref="RulesNotes"/> is, on its file tag in the prompt.</summary>
    public const string RulesLabel = "The verified rules for this game version";

    /// <summary>The most wiki pages the index lists; a last line says how many more there are.</summary>
    public const int WikiIndexLimit = 200;

    /// <summary>The appendix for this harness, after the notes.</summary>
    public const string HarnessNotes =
        "## Playing from inside the game\n\n" +
        "You run inside the game itself, through the SpireMind mod, not in Claude Code. Only tool calls do anything: " +
        "text you write outside `say` is seen by nobody, so keep it short.\n\n" +
        "- The strategy notes the prompt asks you to read are already above under Strategy notes. Don't try to read them.\n" +
        "- The wiki's pages are listed above under Wiki index but not included: read one with `notes_read` when the prompt says to. " +
        "The index stands in for globbing; call `notes_list` only to find pages made since this prompt was built. " +
        "`strategy/wiki/README.md` isn't readable here; the prompt gives its conventions.\n" +
        "- Your only files are `runs/current.md`, `strategy/learned.md` and the wiki's pages, `strategy/wiki/<slug>.md` or " +
        "`strategy/wiki/<folder>/<slug>.md` in lowercase words joined by hyphens: read one with `notes_read`, add a line " +
        "with `notes_append` (each floor's log line, a lesson for a page's `## Lessons` or `strategy/learned.md`) and replace one " +
        "with `notes_write` (the whole file, for the Build plan, Orders, the post-mortem, a new or corrected page; on a page with " +
        "Facts it refuses any change above `## Lessons`). There is no other file access.\n" +
        "- Lessons are held for review: a lesson you add to a page or to `strategy/learned.md` is saved in a pending file, not " +
        "on the page, so you won't see it in `notes_read` or in later prompts. Put a lesson under a page's `## Lessons`, never in its " +
        $"summary or another section. Write each as one line: `{NotesTools.LessonForm}`, " +
        "the source being the fight's id (date, act and floor, as in `2026-10-11 A1 F7`) or the floor.\n" +
        "- `runs/README.md` isn't readable here. The header line it describes goes in exactly this form: " +
        "`# Ironclad, started YYYY-MM-DD, Neow: <what you picked>`, with your character and the date the journal starts, " +
        "which is today's date from the conversation's first message. Archiving is automatic: when you " +
        "write `runs/current.md` with a new header line, or without the post-mortem the old journal has (a new run), the old " +
        "journal is kept as `runs/<date>-<character>.md`, named from its header line, so keep that line unchanged for the rest of a run.\n" +
        "- When the conversation gets long, it is restarted with your run journal, the recent chat and the current state. " +
        "Keep the Build plan, Orders and floor log in `runs/current.md` current so nothing is lost.\n" +
        "- When `act_sequence` is in your tools, it can play a whole turn from one snapshot; see its description.\n" +
        "- Waiting costs nothing here: `wait_for_decision` waits as long as it takes, through enemy turns, teammates and pauses. " +
        "While a run is in progress, every reply must end with a tool call; when there is nothing to do yet, call `wait_for_decision`.";

    /// <summary>
    /// Build the system text from <paramref name="folder"/> (the repo, or a fallback folder with
    /// the same layout).
    /// </summary>
    /// <param name="character">The character being played ("Ironclad", "The Silent"), or null
    /// when unknown; picks <c>strategy/characters/&lt;name&gt;.md</c>.</param>
    /// <param name="multiplayer">Adds <c>strategy/coop.md</c>.</param>
    /// <param name="notesFolder">Where the brain writes its notes, when that isn't
    /// <paramref name="folder"/>. <c>strategy/learned.md</c> is read from there when it exists
    /// there, so lessons written in fallback mode reach the next prompt, and the wiki index
    /// lists the pages there, the ones the notes tools can read.</param>
    public static SystemPrompt Build(string folder, string? character, bool multiplayer, string? notesFolder = null)
    {
        var files = new List<string>();
        var text = new StringBuilder();

        if (TryRead(Path.Combine(folder, PlayPrompt)) is { } play)
        {
            files.Add(Path.GetFullPath(Path.Combine(folder, PlayPrompt)));
            text.Append(StripFrontmatter(play).Replace("$ARGUMENTS", Scope).Trim()).Append("\n\n");
        }

        var notes = new List<(string Name, string Path, string? About)>
        {
            (GeneralNotes, Path.Combine(folder, GeneralNotes), null),
            (RulesNotes, Path.Combine(folder, RulesNotes), RulesLabel),
            (BossNotes, Path.Combine(folder, BossNotes), null),
        };
        if (CharacterFile(character) is { } characterFile)
            notes.Add((characterFile, Path.Combine(folder, characterFile), null));
        var learned = notesFolder is not null && File.Exists(Path.Combine(notesFolder, LearnedNotes))
            ? Path.Combine(notesFolder, LearnedNotes)
            : Path.Combine(folder, LearnedNotes);
        notes.Add((LearnedNotes, learned, null));
        if (multiplayer)
            notes.Add((CoopNotes, Path.Combine(folder, CoopNotes), null));

        var section = new StringBuilder();
        foreach (var (name, path, about) in notes)
        {
            if (TryRead(path) is not { } content || content.Trim().Length == 0)
                continue;
            files.Add(Path.GetFullPath(path));
            var label = about is null ? "" : $" about=\"{about}\"";
            section.Append($"<file path=\"{name}\"{label}>\n").Append(content.Trim()).Append("\n</file>\n\n");
        }
        if (section.Length > 0)
            text.Append("## Strategy notes\n\n").Append(section);

        text.Append(WikiIndex(notesFolder ?? folder)).Append("\n\n");
        text.Append(HarnessNotes).Append('\n');
        return new SystemPrompt(text.ToString(), files);
    }

    /// <summary>
    /// The Wiki index section: one line per page under <paramref name="root"/>, its path and summary
    /// (the path alone when it has none), sorted by path, at most <see cref="WikiIndexLimit"/>
    /// of them. Page text isn't included. The prompt is only rebuilt for a new run or character,
    /// so its cache holds, which makes this the wiki as it stood then.
    /// </summary>
    public static string WikiIndex(string root)
    {
        var pages = NotesTools.ListWiki(root);
        var text = new StringBuilder("## Wiki index\n\n");
        if (pages.Count == 0)
            return text.Append($"The wiki is empty: no pages under `{NotesTools.WikiFolder}/` yet.").ToString();

        text.Append($"Pages under `{NotesTools.WikiFolder}/` when this prompt was built, with their summaries (`notes_list` also shows newer ones):\n\n");
        foreach (var page in pages.Take(WikiIndexLimit))
            text.Append(page.Summary is null ? page.File : $"{page.File}: {page.Summary}").Append('\n');
        if (pages.Count > WikiIndexLimit)
            text.Append($"{pages.Count - WikiIndexLimit} more pages; `notes_list` shows them all.\n");
        return text.ToString().TrimEnd();
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
