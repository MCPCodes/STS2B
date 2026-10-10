using System.Text.Json.Nodes;
using SpireMind.Core;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>The chat hub's co-op additions: teammate lines, relayed lines, the command filter
/// and the /help note.</summary>
public class ChatCoopTests
{
    [Fact]
    public void Teammate_lines_are_logged_as_remote_bot_lines_and_queued_when_for_this_bot()
    {
        var hub = new ChatHub { BotName = "Silent" };
        var added = new List<ChatLine>();
        hub.LineAdded += added.Add;

        var unaddressed = hub.PostFromTeammate("Ironclad", "I'll take   the elite,\nyou keep the gold.");
        Assert.Equal(PostOutcome.Queued, unaddressed.Outcome);
        Assert.NotNull(unaddressed.Seq);
        var addressed = hub.PostFromTeammate("Ironclad", "Silent, poison the big one");
        Assert.Equal(PostOutcome.Queued, addressed.Outcome);
        var elsewhere = hub.PostFromTeammate("Ironclad", "Defect, frost orbs please");
        Assert.Equal(PostOutcome.NotForMe, elsewhere.Outcome);
        Assert.NotNull(elsewhere.Seq);

        var lines = hub.LinesSince(0);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l =>
        {
            Assert.Equal(ChatLineKind.Bot, l.Kind);
            Assert.True(l.Remote);
            Assert.Equal("Ironclad", l.From);
        });
        Assert.Equal("I'll take the elite, you keep the gold.", lines[0].Text);
        Assert.Equal(unaddressed.Seq, lines[0].Seq);
        Assert.Equal(lines, added);

        // A teammate never wakes the bot early, but its lines ride along with the next result.
        Assert.False(hub.HasUnread);
        var taken = hub.TakeUnread();
        Assert.Equal(2, taken.Count);
        Assert.All(taken, m => Assert.True(m.Teammate));
        Assert.Equal("Silent, poison the big one", taken[1].Text);
        Assert.Empty(hub.TakeUnread());
    }

    [Fact]
    public void Teammate_lines_are_never_commands_and_skip_the_burst_limit_and_speech_hold()
    {
        var hub = new ChatHub { BotName = "Silent" };
        hub.AddCommand("model", "", "sets the model", (_, _) => "changed");

        Assert.Equal(PostOutcome.Queued, hub.PostFromTeammate("Defect", "/pause").Outcome);
        Assert.Equal(PostOutcome.Queued, hub.PostFromTeammate("Defect", "/model big").Outcome);
        Assert.False(hub.Paused);
        Assert.Equal(["/pause", "/model big"], hub.LinesSince(0).Select(l => l.Text));

        for (var i = 0; i < 10; i++)
            hub.PostFromTeammate("Defect", $"line {i}");
        Assert.Equal(TimeSpan.Zero, hub.SpeechHoldLeft());
        // The bot's own lines still have their full burst allowance.
        for (var i = 0; i < 6; i++)
            hub.Say($"mine {i}");
        Assert.Throws<InvalidOperationException>(() => hub.Say("one too many"));
    }

    [Fact]
    public void Teammate_posts_trim_the_sender_cut_long_text_and_reject_empty_text()
    {
        var hub = new ChatHub { BotName = "Silent", MaxSayLength = 20 };
        Assert.Equal(PostOutcome.Empty, hub.PostFromTeammate("Defect", "   ").Outcome);
        Assert.Null(hub.PostFromTeammate("Defect", "").Seq);
        Assert.Equal(PostOutcome.Empty, hub.PostFromTeammate("Defect", null!).Outcome);

        hub.PostFromTeammate("  ", "a very long line that goes on and on");
        var line = Assert.Single(hub.LinesSince(0));
        Assert.Equal("Bot", line.From);
        Assert.Equal("a very long line...", line.Text);
    }

    [Fact]
    public void At_most_twenty_teammate_messages_wait_and_human_ones_are_kept_in_order()
    {
        var hub = new ChatHub { BotName = "Silent" };
        hub.PostFromHuman("Mark", "first from Mark");
        for (var i = 0; i < 25; i++)
            hub.PostFromTeammate("Defect", $"party {i}");
        hub.PostFromHuman("Mark", "second from Mark");
        Assert.True(hub.HasUnread);

        var peeked = hub.PeekUnread();
        var taken = hub.TakeUnread();
        Assert.Equal(peeked, taken);
        Assert.Equal(22, taken.Count);
        Assert.Equal("first from Mark", taken[0].Text);
        Assert.False(taken[0].Teammate);
        Assert.Equal("party 5", taken[1].Text);
        Assert.Equal("party 24", taken[20].Text);
        Assert.Equal("second from Mark", taken[21].Text);
        Assert.Equal(20, taken.Count(m => m.Teammate));
        Assert.False(hub.HasUnread);

        // The count starts again after a take.
        hub.PostFromTeammate("Defect", "again");
        Assert.False(hub.HasUnread);
        Assert.Single(hub.TakeUnread());
    }

    [Fact]
    public void Remote_says_and_labelled_announcements_have_their_shapes()
    {
        var hub = new ChatHub { BotName = null, MaxSayLength = 30 };
        var added = new List<ChatLine>();
        hub.LineAdded += added.Add;

        var say = hub.AddRemoteSay(" Silent ", "Poison   is\tticking.");
        Assert.Equal(ChatLineKind.Bot, say.Kind);
        Assert.True(say.Remote);
        Assert.Equal("Silent", say.From);
        Assert.Equal("Poison is ticking.", say.Text);
        var cut = hub.AddRemoteSay("Silent", new string('x', 50));
        Assert.Equal(30, cut.Text.Length);

        var system = hub.AnnounceFrom("Silent", "Silent paused.  Type /resume to hand back control.");
        Assert.Equal(ChatLineKind.System, system.Kind);
        Assert.True(system.Remote);
        Assert.Equal("", system.From);
        Assert.Equal("Silent: Silent paused. Type /resume to hand back control.", system.Text);

        Assert.Equal([say, cut, system], added);
        Assert.Throws<ArgumentException>(() => hub.AddRemoteSay("Silent", "  "));
        Assert.Throws<ArgumentException>(() => hub.AnnounceFrom("Silent", ""));

        // None of them is a message for the model, holds back a move or uses the burst limit.
        Assert.Empty(hub.TakeUnread());
        Assert.Equal(TimeSpan.Zero, hub.SpeechHoldLeft());
        Assert.False(hub.Announce("local").Remote);
        Assert.False(hub.Say("mine").Remote);
    }

    [Fact]
    public void Post_results_carry_the_seq_of_the_line_they_logged()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        hub.AddCommand("quiet", "", "says nothing", (_, _) => "");
        hub.AddCommand("loud", "", "says something", (_, _) => "something");

        var queued = hub.PostFromHuman("Mark", "go");
        Assert.Equal(1, queued.Seq);
        var other = hub.PostFromHuman("Mark", "Silent, go");
        Assert.Equal(2, other.Seq);
        var paused = hub.PostFromHuman("Mark", "/pause");
        Assert.Equal(3, paused.Seq);
        Assert.Equal(hub.LinesSince(2).Single().Text, paused.Reply);
        Assert.Null(hub.PostFromHuman("Mark", "/quiet").Seq);
        Assert.Equal(4, hub.PostFromHuman("Mark", "/loud").Seq);
        Assert.Equal(5, hub.PostFromHuman("Mark", "/nothing").Seq);
        Assert.Null(hub.PostFromHuman("Mark", " ").Seq);
    }

    [Fact]
    public void Command_filter_can_pass_answer_silence_or_fail()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        var ran = new List<string>();
        hub.AddCommand("model", "[name]", "sets the model", (_, args) => { ran.Add(args); return $"model {args}"; });
        var seen = new List<(string From, string Name, string Args)>();
        string? answer = null;
        var fail = false;
        hub.CommandFilter = (from, name, args) =>
        {
            seen.Add((from, name, args));
            if (fail) throw new InvalidOperationException("relay\nbroke");
            return answer;
        };

        // Null: the command runs here as usual, built-in or added.
        var local = hub.PostFromHuman("Mark", "/Model   big ");
        Assert.Equal("model big", local.Reply);
        Assert.Equal(("Mark", "Model", "big"), seen[^1]);
        Assert.Equal(["big"], ran);
        hub.PostFromHuman("Mark", "/pause");
        Assert.True(hub.Paused);
        Assert.Equal(("Mark", "pause", ""), seen[^1]);

        // "": handled elsewhere, no line here.
        answer = "";
        var before = hub.LastSeq;
        var silent = hub.PostFromHuman("Mark", "/resume");
        Assert.Equal(new PostResult(PostOutcome.Command), silent);
        Assert.True(hub.Paused);
        Assert.Equal(before, hub.LastSeq);

        // Text: the reply instead, logged as a system line.
        answer = "  No bot copy is answering yet.  ";
        var replied = hub.PostFromHuman("Mark", "/model small");
        Assert.Equal(PostOutcome.Command, replied.Outcome);
        Assert.Equal("No bot copy is answering yet.", replied.Reply);
        var line = hub.LinesSince(before).Single();
        Assert.Equal(ChatLineKind.System, line.Kind);
        Assert.Equal(line.Seq, replied.Seq);
        Assert.Equal(["big"], ran);

        // Throwing: reported like a failing command.
        fail = true;
        var failed = hub.PostFromHuman("Mark", "/model small");
        Assert.Equal("That didn't work: relay broke", failed.Reply);
        Assert.Equal(["big"], ran);

        // Plain lines never reach the filter.
        var count = seen.Count;
        hub.PostFromHuman("Mark", "just talking");
        hub.PostFromTeammate("Silent", "/model other");
        Assert.Equal(count, seen.Count);

        hub.CommandFilter = null;
        Assert.Equal("model again", hub.PostFromHuman("Mark", "/model again").Reply);
    }

    [Fact]
    public void Help_note_is_added_to_the_help_reply_while_set()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        var plain = hub.PostFromHuman("Mark", "/help").Reply!;
        Assert.EndsWith("to talk to that bot only.", plain);

        hub.HelpNote = "  In co-op, commands go to the bots. ";
        Assert.Equal("In co-op, commands go to the bots.", hub.HelpNote);
        Assert.Equal(plain + " In co-op, commands go to the bots.", hub.PostFromHuman("Mark", "/help").Reply);

        hub.HelpNote = " ";
        Assert.Null(hub.HelpNote);
        Assert.Equal(plain, hub.PostFromHuman("Mark", "/help").Reply);
    }

    [Fact]
    public void Remove_command_takes_off_only_the_handler_given()
    {
        var hub = new ChatHub();
        Func<string, string, string> mine = (_, _) => "mine";
        Func<string, string, string> theirs = (_, _) => "theirs";
        hub.AddCommand("bots", "", "lists bots", mine);
        Assert.False(hub.RemoveCommand("/bots", theirs));
        Assert.Equal("mine", hub.PostFromHuman("Mark", "/bots").Reply);
        Assert.True(hub.RemoveCommand("/BOTS", mine));
        Assert.StartsWith("There's no /bots command", hub.PostFromHuman("Mark", "/bots").Reply);
        hub.AddCommand("bots", "", "lists bots", theirs);
        Assert.True(hub.RemoveCommand("bots"));
        Assert.False(hub.RemoveCommand("bots"));
    }

    [Fact]
    public void Tool_results_mark_teammate_messages()
    {
        var game = new FakeGame();
        var versioner = new SnapshotVersioner();
        var chat = new ChatHub { BotName = "Silent" };
        var tools = new BotTools(game, new DecisionWaiter(game, versioner), versioner, chat);
        chat.PostFromHuman("Mark", "Silent, go poison");
        chat.PostFromTeammate("Ironclad", "I'll tank the elite");

        var state = tools.CallAsync("get_state", new JsonObject(), CancellationToken.None).GetAwaiter().GetResult().Payload;
        var messages = state["chat"]!["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.False(messages[0]!.AsObject().ContainsKey("teammate"));
        Assert.Equal("Ironclad", messages[1]!["from"]!.GetValue<string>());
        Assert.True(messages[1]!["teammate"]!.GetValue<bool>());
        Assert.Empty(chat.TakeUnread());
    }
}
