using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// What a model is told in the arena. The system text is the in-game brain's own: the play
/// prompt, the strategy notes and the index of the wiki's pages, built by
/// <see cref="PromptBuilder"/> from the repo, so a model is judged on the instructions it plays
/// the real game with. Only the brain's closing appendix about its harness is swapped for one
/// about the arena, since it describes notes tools and a run journal that don't exist here. The
/// wiki's pages do: a session opens them with Claude Code's Read tool, as the in-game brain's
/// session can (<see cref="ClaudeCli.Arguments"/>), and the appendix says where they are.
/// </summary>
public static class ArenaPrompt
{
    /// <summary>The character every arena fight is played as; picks the character's strategy file.</summary>
    public const string Character = "Ironclad";

    /// <summary>What stands for the checkout's path where the prompt is hashed
    /// (<see cref="Hash(string, string)"/>).</summary>
    public const string CheckoutMark = "<checkout>";

    /// <summary>
    /// The appendix that replaces <see cref="PromptBuilder.HarnessNotes"/>. It says how the
    /// wiki's pages are read here (files under the strategy folder of <paramref name="repo"/>,
    /// with Claude Code's Read tool, and nothing can be written), what the fight is (the real
    /// game's content, worded by the arena) and names the rules a model can't read off a
    /// snapshot: the end screen and the turn limit (<see cref="Tuning.MaxTurns"/>, read from
    /// the simulator so the two can't drift apart).
    /// </summary>
    /// <param name="repo">The checkout the prompt is built from, by the path the session's Read
    /// rule is given for (<see cref="ClaudeCli.ReadRule"/>): the index above the appendix lists
    /// this checkout's pages, so they are read from here too.</param>
    public static string ArenaNotes(string repo)
    {
        var root = Path.TrimEndingDirectorySeparator(repo).Replace(Path.DirectorySeparatorChar, '/');
        var wiki = $"{root}/{NotesTools.WikiFolder}";
        return
            "## Playing in the arena\n\n" +
            "This session is one practice fight in the SpireMind arena: a simulator with the game's combat rules, " +
            "snapshots and tools, and nothing around the fight. Only tool calls do anything.\n\n" +
            "- The strategy notes the prompt asks you to read are already included above under Strategy notes. Don't read them again.\n" +
            $"- The wiki's pages are listed above under Wiki index but not included. They are files under `{wiki}/`: " +
            $"the index's `{NotesTools.WikiFolder}/enemies/<name>.md` is `{wiki}/enemies/<name>.md`. Read one with the `{ClaudeCli.ReadTool}` tool " +
            $"when the prompt says to. The index stands in for globbing, the `notes_` tools the prompt names don't exist here, and " +
            $"`{ClaudeCli.ReadTool}` opens no file outside that `strategy/` folder.\n" +
            "- Nothing can be written here: no lesson on a wiki page, no `strategy/learned.md`, no journal. There is no run or map either, " +
            "so skip everything the prompt says about adding or fixing Lessons, `runs/current.md`, the Build plan, Orders and the Journal.\n" +
            "- The fight follows the real game (v0.107.1): the Ironclad's cards, act 1's enemies, elites and bosses, and potions, " +
            "with the game's own numbers and rules. The only relic is Burning Blood. Card, power and potion texts are worded by the " +
            "arena, so they read a little differently from the game's; where they and your memory disagree, go by the snapshot.\n" +
            string.Create(CultureInfo.InvariantCulture, $"- The fight counts as lost if you haven't won it by the end of turn {Tuning.MaxTurns}.\n") +
            $"- The fight is the whole session. When it ends, the snapshot's `screen` is `{ArenaGame.OverScreen}`, `state.result` says " +
            $"`won` or `lost`, and the only action left is `{ArenaGame.DoneAction}`, whose label says the fight is over. Nothing follows it " +
            $"and `{ArenaGame.DoneAction}` does nothing, so stop there.";
    }

    /// <summary>The user prompt (<c>-p</c>) of every fight.</summary>
    public const string UserPrompt =
        "This is a practice fight in the SpireMind arena, a simulator that follows the game's combat rules and gives you " +
        "the same tools and snapshots. You are the Ironclad in a single combat; there is no map, reward or run around it.\n\n" +
        "Play the fight to the end as well as you can. Winning comes first; after that, finish with as much HP as possible.\n\n" +
        "- Start with `wait_for_decision`. Play whole turns with `act_sequence` where it is safe to plan them from one snapshot, " +
        "and use `act` for single moves.\n" +
        "- The wiki's pages can be read with `" + ClaudeCli.ReadTool + "`; the system prompt says where they are and when to read one. " +
        "Nothing can be written: no journal, no strategy notes, no wiki lessons.\n" +
        "- Call `say` at most once in the whole fight.\n" +
        "- The fight is over when a snapshot's `screen` is `" + ArenaGame.OverScreen + "`: its `state.result` says `won` or `lost` and the " +
        "only action left is `" + ArenaGame.DoneAction + "`. Then stop: make no more tool calls, and answer with one short line on how it went.";

    /// <summary>
    /// The prompt (<c>-p</c>) a session is started again with (<c>--resume</c>) when it ended
    /// before the fight was over. Claude Code headless ends as soon as the model answers without
    /// a tool call; the in-game brain resumes its session the same way
    /// (<see cref="SubscriptionBrain.ResumePrompt"/>).
    /// </summary>
    public const string ResumePrompt =
        "Keep playing: the fight isn't over. Call wait_for_decision, and go on until a snapshot's `screen` is `" + ArenaGame.OverScreen + "`.";

    /// <summary>
    /// The system text for a fight, from the play prompt and strategy notes under
    /// <paramref name="repo"/> (general, rules, bosses and elites, the Ironclad's, learned) and
    /// the index of its wiki's pages, with <see cref="ArenaNotes"/> for the same checkout last.
    /// <see cref="SystemPrompt.Files"/> lists what it was built from.
    /// </summary>
    public static SystemPrompt Build(string repo)
    {
        var built = PromptBuilder.Build(repo, Character, multiplayer: false);
        var text = built.Text;
        var appendix = PromptBuilder.HarnessNotes + "\n";
        if (text.EndsWith(appendix, StringComparison.Ordinal))
            text = text[..^appendix.Length];
        return built with { Text = text + ArenaNotes(repo) + "\n" };
    }

    /// <summary>
    /// The checkout the prompt is built from and the wiki is read from:
    /// <paramref name="given"/> when it holds the play prompt, else the nearest folder above the
    /// build output or the current folder that does. Null when there is none. It comes back by
    /// its real path, links on the way followed (<see cref="ClaudeCli.RealPath"/>): Claude Code
    /// only lets a file be read when every spelling of its path, the one asked for and the one
    /// its links lead to, is allowed, so the path the prompt names and the Read rule is made
    /// for has to be the one with no links in it.
    /// </summary>
    public static string? FindRepo(string? given)
    {
        if (given is not null)
        {
            var folder = Path.GetFullPath(given);
            return BrainPaths.HasPrompt(folder) ? Real(folder) : null;
        }
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (BrainPaths.HasPrompt(dir.FullName))
                    return Real(dir.FullName);
        return null;

        static string Real(string folder) => Path.TrimEndingDirectorySeparator(ClaudeCli.RealPath(folder));
    }

    /// <summary>The first 12 hex digits of the text's SHA-256: enough to tell two prompts apart
    /// in the results without writing the prompt there.</summary>
    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant();

    /// <summary>
    /// The hash of a system text that <see cref="Build"/> made for <paramref name="repo"/>, with
    /// the checkout's path taken out of its appendix first (<see cref="CheckoutMark"/> in its
    /// place), so two checkouts that hold the same play prompt, strategy notes and wiki index
    /// give the same hash wherever they lie.
    /// </summary>
    public static string Hash(string text, string repo)
    {
        var own = ArenaNotes(repo) + "\n";
        return Hash(text.EndsWith(own, StringComparison.Ordinal) ? text[..^own.Length] + ArenaNotes(CheckoutMark) + "\n" : text);
    }

    /// <summary>
    /// The first 12 hex digits of a SHA-256 over every file under the wiki folder of
    /// <paramref name="repo"/> (names and contents, in the order of their paths), to tell
    /// whether two fights could read the same pages: the pages' text isn't in the prompt, so
    /// the prompt's hash doesn't cover it. Null when the checkout has no wiki folder; a file
    /// that can't be read counts by its name alone.
    /// </summary>
    public static string? WikiHash(string repo)
    {
        var wiki = Path.Combine(repo, NotesTools.WikiFolder.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (!Directory.Exists(wiki))
                return null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var files = Directory.EnumerateFiles(wiki, "*", SearchOption.AllDirectories)
                .Select(path => (Name: Path.GetRelativePath(wiki, path).Replace(Path.DirectorySeparatorChar, '/'), Path: path))
                .OrderBy(file => file.Name, StringComparer.Ordinal);
            foreach (var (name, path) in files)
            {
                hash.AppendData(Encoding.UTF8.GetBytes(name + "\n"));
                try
                {
                    var content = File.ReadAllBytes(path);
                    hash.AppendData(Encoding.UTF8.GetBytes(content.Length.ToString(CultureInfo.InvariantCulture) + "\n"));
                    hash.AppendData(content);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    hash.AppendData("unreadable\n"u8);
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset())[..12].ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
