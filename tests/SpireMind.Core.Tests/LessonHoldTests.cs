using System.Text.Json.Nodes;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>Lessons the bot writes wait in a pending file for review, so an untested lesson
/// never reaches a prompt, notes_read or notes_list.</summary>
public class LessonHoldTests : IDisposable
{
    private const string Learned = "strategy/learned.md";
    private const string Page = "strategy/wiki/enemies/jaw-worm.md";
    private const string PendingPage = "strategy/pending/wiki/enemies/jaw-worm.md";
    private const string PendingLearned = "strategy/pending/learned.md";
    private const string Lesson = "- (Ironclad, 2026-10-11, 2026-10-11 A1 F3) Chomp hit for 11 on turn 1; Defend first.";
    private const string FactsPage =
        "# Jaw Worm\nAn Act 1 brute.\n\n## Facts (game files, v0.107.1)\n- 40-44 HP.\n\n## Lessons\n- (Silent, 2026-10-09, floor 2) Old lesson.\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "spiremind-lessons-" + Guid.NewGuid().ToString("N"));

    public LessonHoldTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private NotesTools Notes(string journal = NotesTools.Journal, bool bot = false) =>
        new(_root, () => new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc),
            new NotesOptions { JournalFile = journal, LockFile = Path.Combine(_root, "locks", "notes.lock"), ReadPagesBeforeRewrite = bot });

    private void WriteFile(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string ReadFile(string relative) => File.ReadAllText(Path.Combine(_root, relative));

    private bool Exists(string relative) => File.Exists(Path.Combine(_root, relative));

    private static ToolOutcome Call(NotesTools notes, string tool, string? file = null, string? content = null)
    {
        var args = new JsonObject();
        if (file is not null) args["file"] = file;
        if (content is not null) args["content"] = content;
        return notes.CallAsync(tool, args, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void AssertOk(ToolOutcome outcome) => Assert.False(outcome.IsError, outcome.Payload.ToJsonString());

    [Fact]
    public void Holding_is_the_default()
    {
        Assert.True(new NotesOptions().HoldLessons);
    }

    [Fact]
    public void An_appended_lesson_for_learned_md_waits_in_its_pending_file()
    {
        WriteFile(Learned, "# Lessons from our own runs\n");
        var notes = Notes();

        var reply = Call(notes, "notes_append", Learned, Lesson + "\n");
        AssertOk(reply);
        Assert.True(reply.Payload["held_for_review"]!.GetValue<bool>());
        Assert.False(reply.Payload["appended"]!.GetValue<bool>());
        var note = reply.Payload["note"]!.GetValue<string>();
        Assert.StartsWith($"Saved for review, not added to {Learned}", note);
        Assert.Contains(PendingLearned, note);

        Assert.Equal("# Lessons from our own runs\n", ReadFile(Learned));
        Assert.Equal($"# Held for review: {Learned}\n\n{Lesson}\n", ReadFile(PendingLearned));

        // The same lesson again isn't added twice; another one is.
        AssertOk(Call(notes, "notes_append", Learned, Lesson));
        AssertOk(Call(notes, "notes_append", Learned, "- (Silent, 2026-10-11, floor 9) Poison stacks up on the Kin."));
        Assert.Equal($"# Held for review: {Learned}\n\n{Lesson}\n- (Silent, 2026-10-11, floor 9) Poison stacks up on the Kin.\n", ReadFile(PendingLearned));
    }

    [Fact]
    public void A_lesson_for_a_page_never_reaches_notes_read_notes_list_or_the_prompt()
    {
        WriteFile(Page, FactsPage);
        WriteFile(Learned, "# Lessons\n");
        WriteFile(".claude/commands/play.md", "Play.\n");
        var notes = Notes();

        AssertOk(Call(notes, "notes_append", Page, Lesson));
        AssertOk(Call(notes, "notes_append", Learned, "- (Ironclad, 2026-10-11, floor 4) LEARNED-LESSON"));

        Assert.Equal(FactsPage, ReadFile(Page));
        Assert.EndsWith(Lesson + "\n", ReadFile(PendingPage));

        var read = Call(notes, "notes_read", Page).Payload["content"]!.GetValue<string>();
        Assert.DoesNotContain("Chomp hit for 11", read);
        var list = Call(notes, "notes_list").Payload.ToJsonString();
        Assert.DoesNotContain("pending", list);
        Assert.DoesNotContain("Chomp hit for 11", list);

        var prompt = PromptBuilder.Build(_root, "Ironclad", multiplayer: false, notesFolder: _root);
        Assert.DoesNotContain("Chomp hit for 11", prompt.Text);
        Assert.DoesNotContain("LEARNED-LESSON", prompt.Text);
        Assert.DoesNotContain("pending", PromptBuilder.WikiIndex(_root));
        Assert.DoesNotContain(prompt.Files, f => f.Contains("pending", StringComparison.Ordinal));

        // The pending files aren't notes files at all.
        foreach (var pending in new[] { PendingPage, PendingLearned })
            Assert.True(Call(notes, "notes_read", pending).IsError);
    }

    [Theory]
    [InlineData("- (Ironclad, 2026-10-10) No source.")]
    [InlineData("- Ironclad, 2026-10-10, floor 3: no brackets.")]
    [InlineData("(Ironclad, 2026-10-10, floor 3) No bullet.")]
    [InlineData("- (Ironclad, 10/10/2026, floor 3) Wrong date.")]
    [InlineData("- (Ironclad, 2026-10-10, floor 3)")]
    [InlineData("- (Ironclad, 2026-10-10, floor 3) Two\n- (Ironclad, 2026-10-10, floor 3) lines.")]
    [InlineData("A plain sentence.")]
    public void A_lesson_in_any_other_form_is_refused_and_nothing_is_written(string content)
    {
        WriteFile(Page, FactsPage);
        var notes = Notes();

        var outcome = Call(notes, "notes_append", Page, content);
        Assert.True(outcome.IsError);
        var error = outcome.Payload["error"]!.GetValue<string>();
        Assert.Contains(NotesTools.LessonForm, error);
        Assert.Contains("2026-10-11 A1 F7", error);
        Assert.Equal(FactsPage, ReadFile(Page));
        Assert.False(Directory.Exists(Path.Combine(_root, "strategy", "pending")));
    }

    [Fact]
    public void Appending_a_heading_to_a_missing_page_points_to_notes_write()
    {
        var outcome = Call(Notes(), "notes_append", "strategy/wiki/lessons/shops.md", "# Shops");
        Assert.True(outcome.IsError);
        Assert.Contains("notes_write", outcome.Payload["error"]!.GetValue<string>());
        Assert.False(Exists("strategy/wiki/lessons/shops.md"));
    }

    [Theory]
    [InlineData("- (Ironclad, 2026-10-11, 2026-10-11 A1 F7) Text.", true)]
    [InlineData("- (The Silent, 2026-10-11, floor 7) Text.", true)]
    [InlineData("- (Necrobinder, 2026-10-11, A2 F23 elite) Osty blocks first.", true)]
    [InlineData("- (Ironclad, 2026-10-11, ) Text.", false)]
    [InlineData("- (ironclad, 2026-10-11, floor 7) Text.", false)]
    [InlineData("- (Ironclad, 2026-10-11, floor (7)) Text.", false)]
    [InlineData("-  (Ironclad, 2026-10-11, floor 7) Text.", false)]
    [InlineData("- (Ironclad, 2026-10-11, floor 7)  ", false)]
    public void The_lesson_form(string line, bool ok) =>
        Assert.Equal(ok, NotesTools.IsLessonLine(line));

    [Theory]
    [InlineData(Learned, PendingLearned)]
    [InlineData(Page, PendingPage)]
    [InlineData("strategy/wiki/potions.md", "strategy/pending/wiki/potions.md")]
    public void Pending_files_mirror_the_path_below_strategy(string file, string pending) =>
        Assert.Equal(pending, NotesTools.PendingFileFor(file));

    [Fact]
    public void The_journal_build_plan_and_orders_are_written_as_before()
    {
        var notes = Notes();
        var bot = Notes("runs/bot-17773/current.md");

        AssertOk(Call(notes, "notes_write", NotesTools.Journal, "# Ironclad, started 2026-10-11, Neow: gold\n\n## Build plan\nStrength.\n\n## Orders\n- Take Inflame.\n"));
        AssertOk(Call(notes, "notes_append", NotesTools.Journal, "F1 Jaw Worm, 74 HP. Lesson: block turn 1."));
        AssertOk(Call(bot, "notes_append", NotesTools.Journal, "F1 Nibbits, 60 HP"));

        Assert.Equal("# Ironclad, started 2026-10-11, Neow: gold\n\n## Build plan\nStrength.\n\n## Orders\n- Take Inflame.\nF1 Jaw Worm, 74 HP. Lesson: block turn 1.\n",
            ReadFile(NotesTools.Journal));
        Assert.Equal("F1 Nibbits, 60 HP\n", ReadFile("runs/bot-17773/current.md"));
        Assert.False(Directory.Exists(Path.Combine(_root, "strategy", "pending")));
    }

    [Fact]
    public void A_rewrite_of_learned_md_removes_at_once_and_holds_what_it_adds()
    {
        WriteFile(Learned, "# Lessons from our own runs\n\n- (Ironclad, 2026-10-01, floor 5) Wrong lesson.\n- (Ironclad, 2026-10-02, floor 8) Kept lesson.\n");
        var notes = Notes();

        var reply = Call(notes, "notes_write", Learned,
            "# Lessons from our own runs\n\n## Combat\n- (Ironclad, 2026-10-02, floor 8) Kept lesson.\n" + Lesson + "\n");
        AssertOk(reply);
        Assert.Equal(Lesson, Assert.Single(reply.Payload["held_for_review"]!.AsArray())!.GetValue<string>());
        Assert.Contains("The rest of the file was written.", reply.Payload["note"]!.GetValue<string>());

        // The wrong lesson is gone, the new heading is in, the new lesson waits.
        Assert.Equal("# Lessons from our own runs\n\n## Combat\n- (Ironclad, 2026-10-02, floor 8) Kept lesson.\n", ReadFile(Learned));
        Assert.EndsWith(Lesson + "\n", ReadFile(PendingLearned));
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(ReadFile(Learned)), reply.Payload["bytes"]!.GetValue<int>());

        // What is on disk counts as seen, so the next write isn't refused.
        AssertOk(Call(notes, "notes_write", Learned, "# Lessons from our own runs\n"));
    }

    [Fact]
    public void A_rewrite_of_a_page_holds_only_the_new_lines_under_lessons()
    {
        WriteFile("strategy/wiki/lessons/shops.md", "# Shops\nWhat shops cost.\n\n## Lessons\n- (Ironclad, 2026-10-01, floor 6) Old.\n");
        var notes = Notes();

        AssertOk(Call(notes, "notes_write", "strategy/wiki/lessons/shops.md",
            "# Shop prices\nWhat each shop item cost in Act 1.\n- Removal costs 75.\n\n## Lessons\n- (Ironclad, 2026-10-01, floor 6) Old.\n" + Lesson + "\n"));

        Assert.Equal("# Shop prices\nWhat each shop item cost in Act 1.\n- Removal costs 75.\n\n## Lessons\n- (Ironclad, 2026-10-01, floor 6) Old.\n",
            ReadFile("strategy/wiki/lessons/shops.md"));
        Assert.EndsWith(Lesson + "\n", ReadFile("strategy/pending/wiki/lessons/shops.md"));

        // A new page keeps its name and summary; its lessons wait too.
        AssertOk(Call(notes, "notes_write", "strategy/wiki/lessons/events.md", "# Events\nWhich events to take.\n\n## Lessons\n" + Lesson + "\n"));
        Assert.Equal("# Events\nWhich events to take.\n\n## Lessons\n", ReadFile("strategy/wiki/lessons/events.md"));
    }

    [Fact]
    public void A_page_write_without_a_lessons_heading_is_refused()
    {
        WriteFile("strategy/wiki/lessons/shops.md", "# Shops\nWhat shops cost.\n\n## Lessons\n");
        var notes = Notes();

        // Dropping the heading would put every added line on the page untested.
        var rewrite = Call(notes, "notes_write", "strategy/wiki/lessons/shops.md", "# Shops\nWhat shops cost.\n" + Lesson + "\n");
        Assert.True(rewrite.IsError);
        Assert.Contains("## Lessons heading", rewrite.Payload["error"]!.GetValue<string>());
        Assert.Equal("# Shops\nWhat shops cost.\n\n## Lessons\n", ReadFile("strategy/wiki/lessons/shops.md"));
        Assert.False(Exists("strategy/pending/wiki/lessons/shops.md"));

        var fresh = Call(notes, "notes_write", "strategy/wiki/lessons/nob.md", "# Gremlin Nob\nDon't play skills into Nob.\n");
        Assert.True(fresh.IsError);
        Assert.False(Exists("strategy/wiki/lessons/nob.md"));

        // learned.md has no sections to keep, and with holding off a page needs no heading.
        AssertOk(Call(notes, "notes_write", Learned, "# Lessons from our own runs\n"));
        var open = new NotesTools(_root, () => DateTime.UtcNow, new NotesOptions { HoldLessons = false });
        AssertOk(Call(open, "notes_write", "strategy/wiki/lessons/nob.md", "# Gremlin Nob\nDon't play skills into Nob.\n"));
    }

    [Fact]
    public void Only_lesson_lines_are_held_and_the_rest_of_a_new_page_is_written()
    {
        var notes = Notes();

        // The summary and a section above Lessons go on the page at once, as the docs say.
        AssertOk(Call(notes, "notes_write", "strategy/wiki/lessons/nob.md",
            "# Gremlin Nob\nDon't play skills into Nob.\n\n## How to fight it\n- Attacks only.\n\n## Lessons\n" + Lesson + "\n"));
        Assert.Equal("# Gremlin Nob\nDon't play skills into Nob.\n\n## How to fight it\n- Attacks only.\n\n## Lessons\n",
            ReadFile("strategy/wiki/lessons/nob.md"));
        Assert.EndsWith(Lesson + "\n", ReadFile("strategy/pending/wiki/lessons/nob.md"));
    }

    [Fact]
    public void A_rewrite_whose_new_lesson_has_another_form_is_refused()
    {
        WriteFile(Page, FactsPage);
        var notes = Notes();

        var outcome = Call(notes, "notes_write", Page, FactsPage + "- (Ironclad) block early\n");
        Assert.True(outcome.IsError);
        Assert.Contains("Nothing was written.", outcome.Payload["error"]!.GetValue<string>());
        Assert.Equal(FactsPage, ReadFile(Page));
        Assert.False(Exists(PendingPage));

        // The Facts check still comes first.
        var facts = Call(notes, "notes_write", Page, FactsPage.Replace("40-44", "50", StringComparison.Ordinal) + Lesson + "\n");
        Assert.Contains("Facts section", facts.Payload["error"]!.GetValue<string>());
        Assert.False(Exists(PendingPage));
    }

    [Fact]
    public void A_bot_copy_still_reads_a_page_before_replacing_it()
    {
        WriteFile(Page, FactsPage);
        var bot = Notes("runs/bot-17773/current.md", bot: true);
        bot.ResetSeen();

        // A held lesson needs no read and changes nothing the guard looks at.
        AssertOk(Call(bot, "notes_append", Page, Lesson));
        var blind = Call(bot, "notes_write", Page, FactsPage + Lesson + "\n");
        Assert.True(blind.IsError);
        Assert.True(blind.Payload["not_read"]!.GetValue<bool>());
        Assert.Equal(FactsPage, ReadFile(Page));

        AssertOk(Call(bot, "notes_read", Page));
        var written = Call(bot, "notes_write", Page, FactsPage.Replace("- (Silent, 2026-10-09, floor 2) Old lesson.\n", "", StringComparison.Ordinal));
        AssertOk(written);
        Assert.Null(written.Payload["held_for_review"]);
        Assert.DoesNotContain("Old lesson", ReadFile(Page));
    }

    [Fact]
    public void Another_copys_held_lesson_doesnt_make_a_read_stale_but_its_rewrite_does()
    {
        WriteFile(Page, FactsPage);
        var a = Notes("runs/bot-17773/current.md", bot: true);
        var b = Notes("runs/bot-17774/current.md", bot: true);
        AssertOk(Call(a, "notes_read", Page));
        AssertOk(Call(b, "notes_read", Page));

        // b's lesson waits for review; the page is untouched, so a's read still holds.
        AssertOk(Call(b, "notes_append", Page, Lesson));
        AssertOk(Call(a, "notes_write", Page, FactsPage + "- (Silent, 2026-10-09, floor 2) Old lesson.\n"));

        // b is now behind a's write and is refused until it reads again.
        var refused = Call(b, "notes_write", Page, FactsPage);
        Assert.True(refused.Payload["changed_since_read"]!.GetValue<bool>());
    }

    [Fact]
    public void The_tool_descriptions_and_the_prompt_say_lessons_are_held()
    {
        var specs = Notes().Specs;
        var append = specs.Single(s => s.Name == "notes_append").Description;
        Assert.Contains(NotesTools.LessonForm, append);
        Assert.Contains("saved for review", append);
        Assert.Contains("saved for review", specs.Single(s => s.Name == "notes_write").Description);
        Assert.Contains("Lessons are held for review", PromptBuilder.HarnessNotes);
        Assert.Contains(NotesTools.LessonForm, PromptBuilder.HarnessNotes);
    }
}
