using System.Text.Json.Nodes;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>Notes shared by several copies: the per-copy journal, the copies' lock and the
/// guard that stops one copy's write from wiping another's change.</summary>
public class NotesGuardTests : IDisposable
{
    private const string Learned = "strategy/learned.md";
    private const string Page = "strategy/wiki/enemies/jaw-worm.md";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "spiremind-notes-guard-" + Guid.NewGuid().ToString("N"));

    public NotesGuardTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string LockFile => Path.Combine(_root, "config", "locks", "notes.lock");

    private NotesTools Notes(string journal = NotesTools.Journal, bool locked = true, bool bot = false) =>
        new(_root, () => new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc),
            new NotesOptions { JournalFile = journal, LockFile = locked ? LockFile : null, ReadPagesBeforeRewrite = bot });

    private void WriteFile(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string ReadFile(string relative) => File.ReadAllText(Path.Combine(_root, relative));

    private static ToolOutcome Call(NotesTools notes, string tool, string? file = null, string? content = null)
    {
        var args = new JsonObject();
        if (file is not null) args["file"] = file;
        if (content is not null) args["content"] = content;
        return notes.CallAsync(tool, args, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void AssertRefused(ToolOutcome outcome, string file)
    {
        Assert.True(outcome.IsError);
        Assert.True(outcome.Payload["changed_since_read"]!.GetValue<bool>());
        Assert.StartsWith($"{file} was changed by someone else (another bot copy, or a hand edit) since you last read it",
            outcome.Payload["error"]!.GetValue<string>());
    }

    private static void AssertOk(ToolOutcome outcome) =>
        Assert.False(outcome.IsError, outcome.Payload.ToJsonString());

    [Fact]
    public void A_write_after_someone_elses_change_is_refused_until_read_again()
    {
        WriteFile(Learned, "- lesson one\n");
        var a = Notes();
        var b = Notes();

        AssertOk(Call(a, "notes_read", Learned));
        AssertOk(Call(b, "notes_read", Learned));
        AssertOk(Call(b, "notes_write", Learned, "- lesson one\n- B's lesson\n"));

        var refused = Call(a, "notes_write", Learned, "- lesson one\n- A's lesson\n");
        AssertRefused(refused, Learned);
        Assert.Equal("- lesson one\n- B's lesson\n", ReadFile(Learned));

        var read = Call(a, "notes_read", Learned);
        Assert.Equal("- lesson one\n- B's lesson\n", read.Payload["content"]!.GetValue<string>());
        AssertOk(Call(a, "notes_write", Learned, "- lesson one\n- B's lesson\n- A's lesson\n"));
        Assert.Equal("- lesson one\n- B's lesson\n- A's lesson\n", ReadFile(Learned));

        // Now B is the one behind.
        AssertRefused(Call(b, "notes_write", Learned, "- B again\n"), Learned);
    }

    [Fact]
    public void What_was_there_when_the_tools_were_made_counts_as_seen()
    {
        WriteFile(Learned, "- old\n");
        WriteFile(Page, "# Jaw Worm\nA brute.\n\n## Lessons\n");
        var a = Notes();

        AssertOk(Call(a, "notes_write", Learned, "- old\n- new\n"));
        AssertOk(Call(a, "notes_write", Page, "# Jaw Worm\nA brute.\n\n## Lessons\n- block early\n"));
        // Its own writes count as seen too.
        AssertOk(Call(a, "notes_write", Learned, "- old\n- new\n- newer\n"));
    }

    [Fact]
    public void A_bot_copy_must_read_a_page_before_replacing_it()
    {
        // Another bot copy's page, there before this copy built its prompt (which lists it
        // without its text), and learned.md, whose text the prompt carries.
        WriteFile(Learned, "- old\n");
        WriteFile(Page, "# Jaw Worm\nA brute.\n\n## Lessons\n- (Silent) block turn 1\n");
        var ironclad = Notes("runs/bot-17773/current.md", bot: true);
        ironclad.ResetSeen(); // a new run's prompt

        var blind = Call(ironclad, "notes_write", Page, "# Jaw Worm\nA brute.\n\n## Lessons\n- (Ironclad) hit hard\n");
        Assert.True(blind.IsError);
        Assert.True(blind.Payload["not_read"]!.GetValue<bool>());
        Assert.StartsWith($"{Page} already exists and you haven't read it in this conversation",
            blind.Payload["error"]!.GetValue<string>());
        Assert.Contains("(Silent) block turn 1", ReadFile(Page));
        AssertOk(Call(ironclad, "notes_write", Learned, "- old\n- new\n"));

        // After a read it may, and a fresh prompt forgets the read.
        AssertOk(Call(ironclad, "notes_read", Page));
        AssertOk(Call(ironclad, "notes_write", Page, "# Jaw Worm\nA brute.\n\n## Lessons\n- (Silent) block turn 1\n- (Ironclad) hit hard\n"));
        AssertOk(Call(ironclad, "notes_write", Page, "# Jaw Worm\nA brute.\n\n## Lessons\n- (Silent) block turn 1\n- (Ironclad) hit hard early\n"));
        ironclad.ResetSeen();
        Assert.True(Call(ironclad, "notes_write", Page, "# Jaw Worm\nBlind.\n").Payload["not_read"]!.GetValue<bool>());

        // A new page and an append need no read.
        AssertOk(Call(ironclad, "notes_append", Page, "- (Ironclad) appended"));
        AssertOk(Call(ironclad, "notes_write", "strategy/wiki/enemies/cultist.md", "# Cultist\nNew.\n"));
    }

    [Fact]
    public void The_human_copy_takes_the_pages_in_its_prompt_as_seen_as_in_solo_play()
    {
        WriteFile(Page, "# Jaw Worm\nA brute.\n\n## Lessons\n");
        var solo = Notes();
        solo.ResetSeen();
        AssertOk(Call(solo, "notes_write", Page, "# Jaw Worm\nA brute.\n\n## Lessons\n- block early\n"));
    }

    [Fact]
    public void A_hand_edit_after_the_tools_were_made_is_protected()
    {
        WriteFile(Learned, "- old\n");
        var a = Notes();
        WriteFile(Learned, "- old\n- edited by hand\n");

        AssertRefused(Call(a, "notes_write", Learned, "- old\n- mine\n"), Learned);
        Assert.Equal("- old\n- edited by hand\n", ReadFile(Learned));
    }

    [Fact]
    public void ResetSeen_takes_the_files_as_they_are_now()
    {
        var a = Notes();
        WriteFile(Learned, "- written after the tools were made\n");
        AssertRefused(Call(a, "notes_write", Learned, "- blind\n"), Learned);

        a.ResetSeen(); // the prompt is built from this content
        AssertOk(Call(a, "notes_write", Learned, "- written after the tools were made\n- mine\n"));
    }

    [Fact]
    public void A_page_created_by_another_copy_refuses_a_blind_write()
    {
        var a = Notes();
        var b = Notes();
        AssertOk(Call(b, "notes_write", Page, "# Jaw Worm\nB's page.\n"));

        AssertRefused(Call(a, "notes_write", Page, "# Jaw Worm\nA's page.\n"), Page);
        Assert.Equal("# Jaw Worm\nB's page.\n", ReadFile(Page));

        AssertOk(Call(a, "notes_read", Page));
        AssertOk(Call(a, "notes_write", Page, "# Jaw Worm\nB's page, merged with A's.\n"));
    }

    [Fact]
    public void A_missing_file_is_always_writable()
    {
        var a = Notes();
        AssertOk(Call(a, "notes_write", Page, "# New page\nFirst.\n"));
        AssertOk(Call(a, "notes_write", Learned, "- first lesson\n"));

        // Deleted by someone else: nothing to lose.
        File.Delete(Path.Combine(_root, Learned));
        AssertOk(Call(a, "notes_write", Learned, "- again\n"));
    }

    [Fact]
    public void Append_is_never_refused_and_never_makes_a_stale_view_fresh()
    {
        WriteFile(Learned, "- one\n");
        var a = Notes();
        var b = Notes();
        AssertOk(Call(b, "notes_append", Learned, "- B's line"));

        // a hasn't seen B's line; its append goes through, without wiping it.
        AssertOk(Call(a, "notes_append", Learned, "- A's line"));
        Assert.Equal("- one\n- B's line\n- A's line\n", ReadFile(Learned));

        // The append didn't count as reading B's line.
        AssertRefused(Call(a, "notes_write", Learned, "- one\n- A's line\n"), Learned);
        Assert.Equal("- one\n- B's line\n- A's line\n", ReadFile(Learned));
    }

    [Fact]
    public void Append_on_a_fresh_view_keeps_it_fresh()
    {
        WriteFile(Learned, "- one\n");
        var a = Notes();
        AssertOk(Call(a, "notes_append", Learned, "- two"));
        AssertOk(Call(a, "notes_write", Learned, "- one\n- two\n- three\n"));

        // Appending creates a missing page, and the creator has seen all of it.
        AssertOk(Call(a, "notes_append", Page, "- (Ironclad, 2026-10-10) a lesson"));
        AssertOk(Call(a, "notes_write", Page, "- (Ironclad, 2026-10-10) a lesson, reworded\n"));
    }

    [Fact]
    public void The_journal_isnt_guarded()
    {
        var a = Notes();
        WriteFile(NotesTools.Journal, "# Ironclad, started 2026-10-10, Neow: gold\n");
        AssertOk(Call(a, "notes_write", NotesTools.Journal, "# Ironclad, started 2026-10-10, Neow: gold\n## Build plan\n"));
    }

    [Fact]
    public void A_single_copy_without_a_lock_file_works_as_before()
    {
        WriteFile(Learned, "- one\n");
        var a = Notes(locked: false);
        AssertOk(Call(a, "notes_read", Learned));
        AssertOk(Call(a, "notes_write", Learned, "- one\n- two\n"));
        AssertOk(Call(a, "notes_append", Learned, "- three"));
        AssertOk(Call(a, "notes_write", Learned, "- one\n- two\n- three\n- four\n"));
        Assert.False(Directory.Exists(Path.Combine(_root, "config")));
    }

    [Fact]
    public void A_bot_copys_journal_is_its_own_file()
    {
        WriteFile(NotesTools.Journal, "# Silent, started 2026-10-09, Neow: the human's journal\n");
        var bot = Notes("runs/bot-17773/current.md");

        Assert.Equal(Path.Combine(_root, "runs", "bot-17773", "current.md"), bot.JournalPath);
        Assert.Equal("", bot.ReadText(NotesTools.Journal));
        var read = Call(bot, "notes_read", NotesTools.Journal);
        Assert.Equal(NotesTools.Journal, read.Payload["file"]!.GetValue<string>());
        Assert.False(read.Payload["exists"]!.GetValue<bool>());

        AssertOk(Call(bot, "notes_write", NotesTools.Journal, "# Ironclad, started 2026-10-10, Neow: max HP\n"));
        AssertOk(Call(bot, "notes_append", NotesTools.Journal, "F1 combat: Jaw Worm, took 5"));

        Assert.Equal("# Ironclad, started 2026-10-10, Neow: max HP\nF1 combat: Jaw Worm, took 5\n", ReadFile("runs/bot-17773/current.md"));
        Assert.Equal("# Silent, started 2026-10-09, Neow: the human's journal\n", ReadFile(NotesTools.Journal));
        Assert.StartsWith("# Ironclad", bot.ReadText(NotesTools.Journal));

        var list = Call(bot, "notes_list").Payload["files"]!.AsArray();
        var journal = list.Single(f => f!["file"]!.GetValue<string>() == NotesTools.Journal)!;
        Assert.True(journal["exists"]!.GetValue<bool>());
        Assert.Equal(new FileInfo(bot.JournalPath).Length, journal["bytes"]!.GetValue<long>());
    }

    [Fact]
    public void A_bot_copys_archives_go_beside_its_journal()
    {
        WriteFile("runs/bot-17773/current.md", "# Ironclad, started 2026-10-09, Neow: gold\n## Post-mortem\nDied to the Guardian.\n");
        var bot = Notes("runs/bot-17773/current.md");

        var reply = Call(bot, "notes_write", NotesTools.Journal, "# Ironclad, started 2026-10-10, Neow: max HP\n");

        AssertOk(reply);
        Assert.Equal("runs/bot-17773/2026-10-09-ironclad.md", reply.Payload["archived_previous_as"]!.GetValue<string>());
        Assert.StartsWith("# Ironclad, started 2026-10-09", ReadFile("runs/bot-17773/2026-10-09-ironclad.md"));
        Assert.False(File.Exists(Path.Combine(_root, "runs", "2026-10-09-ironclad.md")));
    }

    [Fact]
    public void The_default_journal_still_archives_under_runs()
    {
        WriteFile(NotesTools.Journal, "# Ironclad, started 2026-10-09, Neow: gold\n");
        var notes = Notes();
        var reply = Call(notes, "notes_write", NotesTools.Journal, "# Silent, started 2026-10-10, Neow: gold\n");
        Assert.Equal("runs/2026-10-09-ironclad.md", reply.Payload["archived_previous_as"]!.GetValue<string>());
        Assert.Equal(Path.Combine(_root, "runs", "current.md"), notes.JournalPath);
    }

    [Theory]
    [InlineData("runs/Bot/current.md")]
    [InlineData("runs/bot_1/current.md")]
    [InlineData("runs/../current.md")]
    [InlineData("runs/a/b/current.md")]
    [InlineData("runs/bot-17773/journal.md")]
    [InlineData("strategy/learned.md")]
    [InlineData("/tmp/current.md")]
    [InlineData("runs//current.md")]
    [InlineData("runs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/current.md")]
    [InlineData("")]
    public void A_bad_journal_file_throws(string journal)
    {
        Assert.Throws<ArgumentException>(() => Notes(journal));
    }

    [Fact]
    public void A_forty_character_name_is_fine()
    {
        var name = new string('a', 40);
        Assert.EndsWith(Path.Combine(name, "current.md"), Notes($"runs/{name}/current.md").JournalPath);
    }

    [Fact]
    public void The_lock_held_elsewhere_makes_the_tools_busy()
    {
        WriteFile(Learned, "- one\n");
        var a = Notes();
        using (FileLock.Acquire(LockFile, TimeSpan.FromSeconds(1)))
        {
            var started = DateTime.UtcNow;
            var outcome = Call(a, "notes_append", Learned, "- two");
            Assert.True(outcome.IsError);
            Assert.Equal(NotesTools.BusyError, outcome.Payload["error"]!.GetValue<string>());
            Assert.True(DateTime.UtcNow - started >= TimeSpan.FromSeconds(4));
            Assert.Equal("- one\n", ReadFile(Learned));

            // The brain's own read of the journal still works.
            Assert.Equal("", a.ReadText(NotesTools.Journal));
        }
        AssertOk(Call(a, "notes_append", Learned, "- two"));
    }

    [Fact]
    public void The_lock_file_is_created_and_kept()
    {
        var a = Notes();
        AssertOk(Call(a, "notes_list"));
        Assert.True(File.Exists(LockFile));
        AssertOk(Call(a, "notes_write", Learned, "- one\n"));
        Assert.True(File.Exists(LockFile));
    }

    [Fact]
    public async Task Two_copies_appending_at_once_lose_no_line()
    {
        var a = Notes();
        var b = Notes();
        await Task.WhenAll(
            Task.Run(() => { for (var i = 0; i < 20; i++) AssertOk(Call(a, "notes_append", Learned, $"- a{i}")); }),
            Task.Run(() => { for (var i = 0; i < 20; i++) AssertOk(Call(b, "notes_append", Learned, $"- b{i}")); }));

        var lines = ReadFile(Learned).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(40, lines.Length);
        Assert.Equal(40, lines.Distinct().Count());
    }

    [Fact]
    public void The_write_description_mentions_the_guard()
    {
        var write = Notes().Specs.Single(s => s.Name == "notes_write");
        Assert.EndsWith("If someone else changed the file since you last read it, the write is refused: read it again first.", write.Description);
    }
}
