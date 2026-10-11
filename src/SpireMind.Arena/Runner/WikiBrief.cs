using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena run --brief</c>: the wiki's pages about a fight's enemies, put in front of the model
/// when the fight starts instead of hoping it opens them. The first message of the fight ends
/// with a section headed <see cref="Heading"/> that holds, for each different enemy of the
/// fight, the full text of its page under <c>strategy/wiki/enemies/</c>, and for an enemy the
/// wiki has no page for one line that says so. Nothing else is in it.
/// <para>
/// Where this would come from in the real game: the brain. The first snapshot of a fight names
/// its enemies (<c>state.enemies[].name</c>, as the mod's combat reader writes them), and the
/// brain can read every page of the wiki (its notes tools do). So when a fight's first snapshot
/// arrives, the brain can look up each enemy's page and add the pages to that snapshot before
/// the model sees it. That is all this is: the enemies come from the fight's first snapshot and
/// from nowhere else (an enemy that only joins later is not in it, as the brain couldn't know
/// of it either), and the pages are ones the model could open itself with its first calls.
/// </para>
/// <para>
/// A page is found by the enemy's name, not by a guessed file name: line 1 of every page is
/// <c># &lt;Name&gt;</c>, and the lookup is built from the files as they are
/// (<see cref="NotesTools.ListWiki"/> reads the titles).
/// </para>
/// </summary>
/// <param name="Pages">The pages given, as the prompt's index names them
/// (<c>strategy/wiki/enemies/vantom.md</c>), in the order the enemies stand.</param>
/// <param name="Chars">The length of the given pages' text together, in characters.</param>
/// <param name="Missing">The enemies the wiki has no page for (or none that could be read), by name.</param>
/// <param name="Section">The section itself, from its heading to its last line.</param>
public sealed record WikiBrief(IReadOnlyList<string> Pages, int Chars, IReadOnlyList<string> Missing, string Section)
{
    /// <summary>The section's heading.</summary>
    public const string Heading = "What the wiki says about this fight";

    /// <summary>Where the enemies' pages are, as the index names them.</summary>
    public const string EnemyFolder = NotesTools.WikiFolder + "/enemies/";

    /// <summary>The one line of the section for an enemy without a page.</summary>
    public static string NoPage(string enemy) => $"The wiki has no page for {enemy}.";

    /// <summary>
    /// The different enemies a snapshot shows, by name, in the order they stand
    /// (<c>state.enemies[].name</c>). This is what the brain reads off a fight's first snapshot.
    /// </summary>
    public static IReadOnlyList<string> Enemies(GameSnapshot snapshot)
    {
        var names = new List<string>();
        if (snapshot.State["enemies"] is JsonArray enemies)
            foreach (var enemy in enemies)
                if (FightResult.Str((enemy as JsonObject)?["name"])?.Trim() is { Length: > 0 } name && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
        return names;
    }

    /// <summary>The different enemies of a scenario's fight as its first snapshot shows them.</summary>
    public static IReadOnlyList<string> Enemies(ArenaGame game) =>
        Enemies(game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult());

    /// <summary>
    /// The enemy pages of the wiki under <paramref name="repo"/> by the name in their first
    /// line, whatever their files are called. Capitals don't count; of two pages with the same
    /// name the first by path is kept.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PagesByName(string repo)
    {
        var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in NotesTools.ListWiki(repo))
            if (page.File.StartsWith(EnemyFolder, StringComparison.Ordinal) && page.Title is { Length: > 0 } title)
                pages.TryAdd(title, page.File);
        return pages;
    }

    /// <summary>
    /// The brief for a fight against <paramref name="enemies"/> (each different name once, in
    /// the order given), from the wiki under the checkout <paramref name="repo"/> as it is now.
    /// </summary>
    public static WikiBrief For(string repo, IEnumerable<string> enemies)
    {
        var byName = PagesByName(repo);
        var given = new List<string>();
        var missing = new List<string>();
        var chars = 0;
        var section = new StringBuilder($"## {Heading}\n\n");
        foreach (var enemy in enemies.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (byName.TryGetValue(enemy, out var file) && ReadPage(repo, file) is { } text)
            {
                given.Add(file);
                chars += text.Length;
                section.Append($"<file path=\"{file}\">\n").Append(text).Append("\n</file>\n\n");
            }
            else
            {
                missing.Add(enemy);
                section.Append(NoPage(enemy)).Append("\n\n");
            }
        }
        return new WikiBrief(given, chars, missing, section.ToString().TrimEnd());
    }

    // A page's whole text without the blank lines around it, or null when it can't be read.
    private static string? ReadPage(string repo, string file)
    {
        try
        {
            var text = File.ReadAllText(Path.Combine(repo, file.Replace('/', Path.DirectorySeparatorChar))).Trim();
            return text.Length > 0 ? text.Replace("\r\n", "\n") : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
