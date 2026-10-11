using System.Collections.Concurrent;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class BrainControllerTests
{
    public const string Key = "sk-ant-api03-controller-test-key";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private const string NoKeyPlaying = "Ironclad is playing on your Claude subscription, since there's no API key. /stop to take over.";

    // ---- /start ----

    [Fact]
    public async Task Start_with_a_key_plays_on_the_api_credit_and_saves_autostart()
    {
        using var rig = new Rig();
        rig.Spend.Add(3.2m, rig.Now);
        await rig.Init();

        var reply = rig.Post("/start");

        Assert.Equal("Ironclad is playing on your API credit (Opus 5.5, high effort, $3.20 of $200 spent this month). /stop to take over.", reply);
        var api = Assert.Single(rig.ApiBrains);
        Assert.True(api.IsRunning);
        Assert.True(api.Settings.HasApiKey);
        Assert.Empty(rig.SubBrains);
        Assert.True(Assert.Single(rig.Saved).Autostart);
        Assert.True(rig.Config.Autostart);
        Assert.True(rig.Controller.Settings.Autostart);
        Assert.Equal(BrainKind.Api, rig.Controller.Playing);
    }

    [Fact]
    public async Task Start_without_a_key_goes_straight_to_the_subscription_during_a_run()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();
        await rig.Poll("combat");

        Assert.Equal(NoKeyPlaying, rig.Post("/start"));

        Assert.Empty(rig.ApiBrains);
        Assert.True(Assert.Single(rig.SubBrains).IsRunning);
        Assert.Equal(BrainKind.Subscription, rig.Controller.Playing);
    }

    [Theory]
    [InlineData(250, 200, "this month's API budget is used up")]
    [InlineData(200, 200, "this month's API budget is used up")]
    [InlineData(0, 0, "the monthly API budget is set to $0")]
    public async Task Start_over_the_cap_goes_straight_to_the_subscription(int spent, int budget, string reason)
    {
        using var rig = new Rig();
        rig.Config = rig.Config with { MonthlyBudgetUsd = budget };
        if (spent > 0) rig.Spend.Add(spent, rig.Now);
        await rig.Init();
        await rig.Poll("map");

        Assert.Equal($"Ironclad is playing on your Claude subscription, since {reason}. /stop to take over.", rig.Post("/start"));
        Assert.Empty(rig.ApiBrains);
        Assert.Single(rig.SubBrains);
    }

    [Fact]
    public async Task The_subscription_waits_for_a_run_and_says_so_when_it_starts()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();

        Assert.Equal("Ironclad will play on your Claude subscription once a run is in progress, since there's no API key.", rig.Post("/start"));
        Assert.Empty(rig.SubBrains);
        Assert.Equal(BrainKind.None, rig.Controller.Playing);

        await rig.Poll("menu");
        await rig.Poll(null);
        Assert.Empty(rig.SubBrains);

        var mark = rig.Mark();
        await rig.Poll("map");
        Assert.True(Assert.Single(rig.SubBrains).IsRunning);
        Assert.Equal([NoKeyPlaying], rig.LinesSince(mark));

        // Said once: the next run starts it quietly.
        rig.SubBrains[0].Finish(BrainStopReason.RunEnded);
        await rig.Poll("game_over");
        await rig.Poll("map");
        Assert.Equal(2, rig.SubBrains.Length);
        Assert.Equal([NoKeyPlaying], rig.LinesSince(mark));
    }

    [Fact]
    public async Task Start_while_a_brain_plays_says_so()
    {
        using var rig = new Rig();
        await rig.Init();
        rig.Post("/start");

        Assert.Equal("Ironclad is already playing on your API credit. /stop to take over.", rig.Post("/start"));
        Assert.Single(rig.ApiBrains);
    }

    [Fact]
    public async Task Start_after_the_api_failed_this_run_uses_the_subscription()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        rig.ApiBrains[0].Finish(BrainStopReason.OutOfCredit);
        var sub = Assert.Single(rig.SubBrains);
        rig.Post("/stop");
        await sub.StopCalled.Task.WaitAsync(Guard);

        Assert.Equal("Ironclad is playing on your Claude subscription, since the API failed this run. /stop to take over.", rig.Post("/start"));
        await Rig.WaitUntil(() => rig.SubBrains.Length == 2 && rig.SubBrains[1].IsRunning);
        Assert.Single(rig.ApiBrains);
    }

    [Fact]
    public async Task The_bot_is_named_generically_without_a_character()
    {
        using var rig = new Rig();
        rig.Chat.BotName = null;
        await rig.Init();
        Assert.StartsWith("The bot is playing on your API credit", rig.Post("/start"));
        Assert.Equal("The bot stopped. /start hands control back.", rig.Post("/stop"));
    }

    [Fact]
    public async Task A_failed_save_is_mentioned_and_the_change_still_holds()
    {
        using var rig = new Rig { SaveThrows = true };
        await rig.Init();

        var reply = rig.Post("/start")!;

        Assert.EndsWith(" Couldn't save that to config.json (IOException), so it only lasts until the game closes.", reply);
        Assert.True(Assert.Single(rig.ApiBrains).IsRunning);
        Assert.True(rig.Controller.Settings.Autostart);
        Assert.Contains(rig.Logged, l => l.Contains("couldn't save config.json"));
    }

    // ---- /stop ----

    [Fact]
    public async Task Stop_stops_the_brain_off_the_calling_thread_and_saves()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var api = rig.ApiBrains[0];
        var mark = rig.Mark();

        // A StopAsync that holds up its caller (it might wait on the main thread) can't hold up /stop.
        api.BlockStop = new ManualResetEventSlim();
        var reply = await Task.Run(() => rig.Post("/stop")).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Ironclad stopped. /start hands control back.", reply);
        await api.StopCalled.Task.WaitAsync(Guard);
        Assert.True(api.IsRunning);
        api.BlockStop.Set();
        await Rig.WaitUntil(() => !api.IsRunning);

        Assert.False(rig.Saved[^1].Autostart);
        Assert.False(rig.Controller.Settings.Autostart);
        Assert.Equal(BrainKind.None, rig.Controller.Playing);
        Assert.Equal(["Ironclad stopped. /start hands control back."], rig.LinesSince(mark));

        // Autostart is off, so the poll loop starts nothing.
        await rig.Poll("combat");
        await rig.Poll("menu");
        await rig.Poll("map");
        Assert.Single(rig.ApiBrains);
        Assert.Empty(rig.SubBrains);
        Assert.Equal("Ironclad isn't playing.", rig.Post("/stop"));
    }

    [Fact]
    public async Task Stop_while_waiting_for_a_run_keeps_the_subscription_from_starting()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();
        rig.Post("/start");

        Assert.Equal("Ironclad isn't playing, and won't start by itself until you type /start.", rig.Post("/stop"));
        await rig.Poll("map");
        Assert.Empty(rig.SubBrains);
    }

    [Fact]
    public async Task Stop_during_a_running_brain_never_falls_back()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var api = rig.ApiBrains[0];
        api.HoldStop = new TaskCompletionSource();
        var mark = rig.Mark();

        rig.Post("/stop");
        await api.StopCalled.Task.WaitAsync(Guard);
        // The brain hits the credit limit while stopping: too late to matter.
        api.Finish(BrainStopReason.OutOfCredit, "The API credit has run out.");
        api.HoldStop.SetResult();
        await rig.Poll("combat");

        Assert.Empty(rig.SubBrains);
        Assert.Equal(["Ironclad stopped. /start hands control back."], rig.LinesSince(mark));
        Assert.Equal(BrainKind.None, rig.Controller.Playing);
    }

    [Fact]
    public async Task A_replaced_brains_stop_is_ignored_and_the_next_brain_waits_for_it()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var first = rig.ApiBrains[0];
        first.HoldStop = new TaskCompletionSource();
        rig.Post("/stop");
        await first.StopCalled.Task.WaitAsync(Guard);
        var mark = rig.Mark();

        Assert.StartsWith("Ironclad is playing on your API credit", rig.Post("/start"));
        var second = rig.ApiBrains[1];
        Assert.Equal(0, second.Starts); // the first one hasn't stopped yet
        Assert.Equal(BrainKind.Api, rig.Controller.Playing);

        first.Finish(BrainStopReason.SpendLimit, "The Console spend limit was reached.");
        Assert.Equal(BrainKind.Api, rig.Controller.Playing);
        first.HoldStop.SetResult();
        await Rig.WaitUntil(() => second.IsRunning);

        Assert.Empty(rig.SubBrains);
        Assert.Single(rig.LinesSince(mark)); // only the /start reply
        Assert.Contains(rig.Logged, l => l.Contains("a replaced brain stopped (SpendLimit)"));
    }

    [Fact]
    public async Task A_brain_that_wont_stop_is_aborted()
    {
        using var rig = new Rig { StopWaitLimit = TimeSpan.FromMilliseconds(50) };
        await rig.Init();
        rig.Post("/start");
        var api = rig.ApiBrains[0];
        api.HoldStop = new TaskCompletionSource(); // never released

        rig.Post("/stop");

        await Rig.WaitUntil(() => api.Aborts == 1);
        Assert.Contains(rig.Logged, l => l.Contains("didn't stop within"));
    }

    [Fact]
    public async Task Stop_clears_the_thinking_mark_at_once_and_again_once_the_brain_has_stopped()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var api = rig.ApiBrains[0];
        api.HoldStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Chat.MarkThinking(true);

        rig.Post("/stop");
        Assert.Null(rig.Chat.ThinkingSince);

        // A tool result already on its way marks it again before the brain has stopped.
        await api.StopCalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        rig.Chat.MarkThinking(true);
        api.HoldStop.SetResult();

        await Rig.WaitUntil(() => rig.Chat.ThinkingSince is null);
        Assert.False(api.IsRunning);
    }

    [Theory]
    [InlineData(BrainStopReason.Error)]
    [InlineData(BrainStopReason.OutOfCredit)]
    public async Task A_brain_that_stops_by_itself_clears_the_thinking_mark(BrainStopReason reason)
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        rig.Chat.MarkThinking(true);

        rig.ApiBrains[0].Finish(reason, "The API brain said why.");

        Assert.Null(rig.Chat.ThinkingSince);
    }

    // ---- Falling back ----

    [Theory]
    [InlineData(BrainStopReason.NoKey)]
    [InlineData(BrainStopReason.Unauthorized)]
    [InlineData(BrainStopReason.OutOfCredit)]
    [InlineData(BrainStopReason.SpendLimit)]
    [InlineData(BrainStopReason.MonthlyCap)]
    public async Task The_api_brain_falls_back_to_the_subscription_for_the_rest_of_the_run(BrainStopReason reason)
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var mark = rig.Mark();

        rig.ApiBrains[0].Finish(reason, "The API brain said why.");

        Assert.Equal([BrainController.SwitchingLine], rig.LinesSince(mark));
        Assert.True(Assert.Single(rig.SubBrains).IsRunning);
        Assert.Equal(BrainKind.Subscription, rig.Controller.Playing);

        // The rest of the run stays on the subscription.
        await rig.Poll("combat");
        await rig.Poll("rewards");
        Assert.Single(rig.ApiBrains);
        Assert.Single(rig.SubBrains);
        Assert.Equal([BrainController.SwitchingLine], rig.LinesSince(mark));
    }

    [Fact]
    public async Task A_fallback_between_runs_holds_for_the_next_run()
    {
        using var rig = new Rig();
        await rig.Init();
        rig.Post("/start");
        var mark = rig.Mark();

        rig.ApiBrains[0].Finish(BrainStopReason.Unauthorized, "The API key was rejected.");
        Assert.Equal([BrainController.SwitchingNextRunLine], rig.LinesSince(mark));
        Assert.Empty(rig.SubBrains);

        await rig.Poll("map");
        Assert.True(Assert.Single(rig.SubBrains).IsRunning);
        Assert.Single(rig.ApiBrains);

        // The run after that tries the API again.
        rig.SubBrains[0].Finish(BrainStopReason.RunEnded);
        await rig.Poll("game_over");
        await rig.Poll("map");
        Assert.Equal(2, rig.ApiBrains.Length);
        Assert.True(rig.ApiBrains[1].IsRunning);
        Assert.Equal([BrainController.SwitchingNextRunLine, BrainController.BackOnApiLine], rig.LinesSince(mark));
    }

    [Theory]
    [InlineData(BrainStopReason.Error)]
    [InlineData(BrainStopReason.UserStop)]
    public async Task Error_and_user_stop_dont_fall_back_and_wait_for_the_next_run(BrainStopReason reason)
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var mark = rig.Mark();

        rig.ApiBrains[0].Finish(reason, "The API brain hit an error and stopped.");

        Assert.Empty(rig.LinesSince(mark));
        Assert.Equal(BrainKind.None, rig.Controller.Playing);
        await rig.Poll("combat");
        await rig.Poll("event");
        Assert.Single(rig.ApiBrains);
        Assert.Empty(rig.SubBrains);
        Assert.EndsWith("Nothing is playing until the next run starts.", rig.Post("/budget"));

        await rig.Poll("game_over");
        await rig.Poll("map");
        Assert.Equal(2, rig.ApiBrains.Length);
        Assert.True(rig.ApiBrains[1].IsRunning);
        Assert.Empty(rig.SubBrains);
    }

    [Fact]
    public async Task A_subscription_error_holds_until_start()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");

        rig.SubBrains[0].Finish(BrainStopReason.Error, "The subscription isn't signed in.");
        await rig.Poll("combat");
        await rig.Poll("combat");
        Assert.Single(rig.SubBrains);

        Assert.Equal(NoKeyPlaying, rig.Post("/start"));
        Assert.Equal(2, rig.SubBrains.Length);
        Assert.True(rig.SubBrains[1].IsRunning);
    }

    [Fact]
    public async Task The_subscription_restarts_while_the_run_goes_on_up_to_a_limit()
    {
        using var rig = new Rig { Key = null, MaxRestartsPerRun = 2 };
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");

        rig.SubBrains[0].Finish(BrainStopReason.RunEnded);
        await rig.Poll("combat");
        Assert.Equal(2, rig.SubBrains.Length);
        rig.SubBrains[1].Finish(BrainStopReason.RunEnded);
        await rig.Poll(null); // a modal: still in the run
        Assert.Equal(3, rig.SubBrains.Length);
        rig.SubBrains[2].Finish(BrainStopReason.RunEnded);
        await rig.Poll("combat");
        await rig.Poll("combat");
        Assert.Equal(3, rig.SubBrains.Length);
        Assert.Single(rig.Logged, l => l.Contains("keeps stepping back"));

        // A new run starts it again; a run that really ended doesn't.
        await rig.Poll("game_over");
        await rig.Poll("map");
        Assert.Equal(4, rig.SubBrains.Length);
        rig.SubBrains[3].Finish(BrainStopReason.RunEnded);
        await rig.Poll("game_over");
        Assert.Equal(4, rig.SubBrains.Length);
    }

    [Fact]
    public async Task The_subscription_brain_is_stopped_once_the_run_has_been_over_for_a_while()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var sub = Assert.Single(rig.SubBrains);
        var mark = rig.Mark();

        // Game over, then the menu: time for the post-mortem first.
        await rig.Poll("game_over");
        rig.Now += TimeSpan.FromSeconds(90);
        await rig.Poll("menu");
        await rig.Poll(null); // a modal at the menu says nothing
        Assert.True(sub.IsRunning);
        Assert.Equal(0, sub.StopCalls);

        rig.Now += TimeSpan.FromSeconds(30);
        await rig.Poll("menu");
        await Rig.WaitUntil(() => !sub.IsRunning);
        Assert.Equal(1, sub.StopCalls);
        Assert.Equal(BrainKind.None, rig.Controller.Playing);
        Assert.Empty(rig.LinesSince(mark));
        Assert.Contains(rig.Logged, l => l.Contains("the run has been over for 120 s; stopping the subscription brain"));
        Assert.EndsWith("Nothing is playing yet. Your Claude subscription takes over once a run is in progress, since there's no API key.",
            rig.Post("/budget"));

        // Nothing starts at the menu; the next run starts it again, quietly.
        mark = rig.Mark();
        rig.Now += TimeSpan.FromMinutes(10);
        await rig.Poll("menu");
        Assert.Single(rig.SubBrains);
        await rig.Poll("map");
        Assert.Equal(2, rig.SubBrains.Length);
        Assert.True(rig.SubBrains[1].IsRunning);
        Assert.Equal(BrainKind.Subscription, rig.Controller.Playing);
        Assert.Empty(rig.LinesSince(mark));
    }

    [Fact]
    public async Task A_run_continued_within_the_grace_keeps_the_subscription_brain()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var sub = Assert.Single(rig.SubBrains);

        // Save and quit, then continue: the clock starts again the next time the run is over.
        await rig.Poll("menu");
        rig.Now += TimeSpan.FromSeconds(90);
        await rig.Poll("map");
        rig.Now += TimeSpan.FromSeconds(90);
        await rig.Poll("menu");
        rig.Now += TimeSpan.FromSeconds(90);
        await rig.Poll("menu");

        Assert.True(sub.IsRunning);
        Assert.Equal(0, sub.StopCalls);
        Assert.Single(rig.SubBrains);
        Assert.Equal(BrainKind.Subscription, rig.Controller.Playing);
    }

    [Fact]
    public async Task The_api_brain_is_left_at_the_menu()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var api = Assert.Single(rig.ApiBrains);

        await rig.Poll("game_over");
        rig.Now += TimeSpan.FromMinutes(10);
        await rig.Poll("menu");

        Assert.True(api.IsRunning);
        Assert.Equal(0, api.StopCalls);
    }

    [Fact]
    public async Task A_fallback_at_a_run_start_the_poll_hasnt_seen_switches_for_this_run()
    {
        using var rig = new Rig();
        await rig.Init();
        rig.Post("/start"); // at the menu, where the API brain idles
        var mark = rig.Mark();
        var api = rig.ApiBrains[0];

        // The run started, and the brain's first request failed before the poll saw it.
        api.LastSeenInRun = true;
        api.Finish(BrainStopReason.OutOfCredit, "The API credit has run out.");

        Assert.Equal([BrainController.SwitchingLine], rig.LinesSince(mark));
        var sub = Assert.Single(rig.SubBrains);
        Assert.True(sub.IsRunning);

        // The poll catches up: the same run, so the subscription plays on.
        await rig.Poll("event");
        Assert.True(sub.IsRunning);
        Assert.Equal(0, sub.StopCalls);
        Assert.Single(rig.ApiBrains);
        Assert.Single(rig.SubBrains);
        Assert.Equal([BrainController.SwitchingLine], rig.LinesSince(mark));
        Assert.Contains("as the fallback until the next run", rig.Post("/budget"));

        // The run after it is back on the API.
        sub.Finish(BrainStopReason.RunEnded);
        await rig.Poll("game_over");
        await rig.Poll("map");
        Assert.Equal(2, rig.ApiBrains.Length);
        Assert.True(rig.ApiBrains[1].IsRunning);
        Assert.Equal(BrainController.BackOnApiLine, rig.LinesSince(mark)[^1]);
    }

    [Fact]
    public async Task A_fallback_after_a_game_over_the_poll_hasnt_seen_waits_for_the_next_run()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var mark = rig.Mark();

        // The run ended, and a request after it failed before the poll saw the game over.
        rig.ApiBrains[0].LastSeenInRun = false;
        rig.ApiBrains[0].Finish(BrainStopReason.OutOfCredit, "The API credit has run out.");

        Assert.Equal([BrainController.SwitchingNextRunLine], rig.LinesSince(mark));
        Assert.Empty(rig.SubBrains);

        await rig.Poll("game_over");
        Assert.Empty(rig.SubBrains);
        await rig.Poll("map");
        Assert.True(Assert.Single(rig.SubBrains).IsRunning);
        Assert.Single(rig.ApiBrains);
    }

    // ---- New runs ----

    [Fact]
    public async Task A_new_run_tries_the_api_again_and_resets_the_run_spend()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        rig.Spend.Add(1.5m, rig.Now);
        rig.ApiBrains[0].Finish(BrainStopReason.OutOfCredit, "The API credit has run out.");
        var sub = Assert.Single(rig.SubBrains);
        Assert.Equal(1.5m, rig.Spend.RunTotal);

        await rig.Poll("game_over");
        Assert.True(sub.IsRunning);
        var mark = rig.Mark();
        await rig.Poll("map");

        Assert.Equal(1, sub.StopCalls);
        Assert.False(sub.IsRunning);
        Assert.Equal(2, rig.ApiBrains.Length);
        Assert.True(rig.ApiBrains[1].IsRunning);
        Assert.Equal(BrainKind.Api, rig.Controller.Playing);
        Assert.Equal([BrainController.BackOnApiLine], rig.LinesSince(mark));
        Assert.Equal(0m, rig.Spend.RunTotal);
        Assert.Equal(0, rig.Spend.Calls);
        Assert.Equal(1.5m, rig.Spend.MonthTotal(rig.Now));
    }

    [Fact]
    public async Task A_key_added_meanwhile_is_used_from_the_next_run()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var sub = rig.SubBrains[0];

        rig.Key = Key;
        await rig.Poll("combat");
        Assert.Empty(rig.ApiBrains); // not mid-run
        await rig.Poll("menu");
        var mark = rig.Mark();
        await rig.Poll("map");

        Assert.False(sub.IsRunning);
        Assert.True(Assert.Single(rig.ApiBrains).IsRunning);
        Assert.Equal(["Ironclad is playing on your API credit (Opus 5.5, high effort, $0.00 of $200 spent this month). /stop to take over."],
            rig.LinesSince(mark));
    }

    [Fact]
    public async Task A_failing_screen_read_keeps_the_last_answer()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();
        await rig.Poll("combat");

        rig.ScreenThrows = true;
        await rig.Poll("menu");
        await rig.Poll("menu");

        Assert.Single(rig.Logged, l => l.Contains("couldn't read the screen"));
        Assert.Equal(NoKeyPlaying, rig.Post("/start")); // still in the run
    }

    // ---- Initialize and Shutdown ----

    [Fact]
    public async Task Initialize_with_autostart_starts_without_saving_or_a_chat_line()
    {
        using var rig = new Rig();
        rig.Config = rig.Config with { Autostart = true };

        await rig.Init();

        Assert.True(Assert.Single(rig.ApiBrains).IsRunning);
        Assert.Empty(rig.Saved);
        Assert.Empty(rig.Chat.LinesSince(0));
        Assert.Contains(rig.Logged, l => l.Contains("autostart is on. Ironclad is playing on your API credit"));
    }

    [Fact]
    public async Task Initialize_with_autostart_and_no_key_waits_for_a_run_and_says_who_plays_once()
    {
        using var rig = new Rig { Key = null };
        rig.Config = rig.Config with { Autostart = true };

        await rig.Init();
        Assert.Empty(rig.SubBrains);
        Assert.Empty(rig.Chat.LinesSince(0));
        await rig.Poll("map");

        Assert.True(Assert.Single(rig.SubBrains).IsRunning);
        Assert.Equal([NoKeyPlaying], rig.LinesSince(0));
    }

    [Fact]
    public async Task Autostart_says_who_plays_when_the_first_run_of_the_session_starts()
    {
        using var rig = new Rig();
        rig.Config = rig.Config with { Autostart = true };

        await rig.Init(); // the API brain idles at the menu: nothing in the chat yet
        Assert.True(Assert.Single(rig.ApiBrains).IsRunning);
        Assert.Empty(rig.Chat.LinesSince(0));

        await rig.Poll("map"); // Mark clicks Continue
        Assert.Equal(["Ironclad is playing on your API credit (Opus 5.5, high effort, $0.00 of $200 spent this month). /stop to take over."],
            rig.LinesSince(0));

        // Once per game session: the next run starts quietly.
        var mark = rig.Mark();
        await rig.Poll("game_over");
        await rig.Poll("map");
        Assert.Empty(rig.LinesSince(mark));
        Assert.Single(rig.ApiBrains);
    }

    [Fact]
    public async Task Start_is_refused_while_another_client_plays_through_the_mod()
    {
        using var rig = new Rig { OtherClient = true };
        await rig.Init();
        await rig.Poll("map");

        Assert.Equal(BrainController.OtherClientLine, rig.Post("/start"));
        Assert.Empty(rig.ApiBrains);
        Assert.Empty(rig.Saved); // autostart stays off
        Assert.False(rig.Controller.Settings.Autostart);

        rig.OtherClient = false;
        Assert.StartsWith("Ironclad is playing on your API credit", rig.Post("/start"));
    }

    [Fact]
    public async Task Stop_tells_running_tool_calls()
    {
        using var rig = new Rig();
        await rig.Init();
        rig.Post("/start");
        var before = rig.Chat.Stops;

        rig.Post("/stop");

        Assert.Equal(before + 1, rig.Chat.Stops);
    }

    [Fact]
    public async Task Initialize_without_autostart_starts_nothing()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("map");
        await rig.Poll("combat");

        Assert.Empty(rig.ApiBrains);
        Assert.Empty(rig.SubBrains);
        Assert.EndsWith("Nothing is playing. /start hands control over.", rig.Post("/budget"));
    }

    [Fact]
    public async Task Shutdown_aborts_every_brain_and_stops_polling()
    {
        using var rig = new Rig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var api = rig.ApiBrains[0];
        api.HoldStop = new TaskCompletionSource();
        rig.Post("/stop");
        await api.StopCalled.Task.WaitAsync(Guard);
        rig.Key = null;
        Assert.Equal(NoKeyPlaying, rig.Post("/start")); // starts once the API brain has stopped
        var sub = Assert.Single(rig.SubBrains);
        Assert.Equal(0, sub.Starts);

        rig.Controller.Shutdown();

        Assert.Equal(1, api.Aborts);
        Assert.Equal(1, sub.Aborts);
        await rig.PollCancelled.Task.WaitAsync(Guard);
        Assert.Equal(BrainKind.None, rig.Controller.Playing);

        // The API brain finally stops: the subscription brain still doesn't start.
        api.HoldStop.SetResult();
        await Task.Delay(100);
        Assert.Equal(0, sub.Starts);
        Assert.Equal("The game is closing.", rig.Post("/start"));
    }

    // ---- /budget, /model, /effort, /help ----

    [Fact]
    public async Task Budget_shows_the_spend_the_model_and_what_plays()
    {
        using var rig = new Rig();
        rig.Spend.Add(3.2m, rig.Now);
        await rig.Init();
        await rig.Poll("combat");
        rig.Spend.Add(0.5m, rig.Now);

        Assert.Equal("API spend: $3.70 of $200 this month, $0.50 this run in 1 request. Opus 5.5, high effort.\nNothing is playing. /start hands control over.",
            rig.Post("/budget"));

        rig.Post("/start");
        Assert.EndsWith("\nPlaying on your API credit.", rig.Post("/budget"));

        rig.ApiBrains[0].Finish(BrainStopReason.OutOfCredit);
        Assert.EndsWith("\nPlaying on your Claude subscription as the fallback until the next run.", rig.Post("/budget"));
    }

    [Fact]
    public async Task Budget_says_when_the_subscription_waits_for_a_run()
    {
        using var rig = new Rig { Key = null };
        rig.Config = rig.Config with { MonthlyBudgetUsd = 12.5m };
        await rig.Init();
        rig.Post("/start");

        var budget = rig.Post("/budget")!;
        Assert.StartsWith("API spend: $0.00 of $12.50 this month, $0.00 this run in 0 requests.", budget);
        Assert.EndsWith("\nNothing is playing yet. Your Claude subscription takes over once a run is in progress, since there's no API key.", budget);

        await rig.Poll("map");
        Assert.EndsWith("\nPlaying on your Claude subscription, since there's no API key.", rig.Post("/budget"));
    }

    [Fact]
    public async Task Model_shows_switches_and_refuses_unknown_models()
    {
        using var rig = new Rig();
        await rig.Init();
        rig.Post("/start");
        var saves = rig.Saved.Length;

        Assert.Equal("The model is Opus 5.5 (claude-opus-5-5). Options: opus, sonnet, haiku, fable, or a full model id.", rig.Post("/model"));

        Assert.Equal("Model set to Sonnet 5.5. It applies from the next request.", rig.Post("/model Sonnet"));
        Assert.Equal("claude-sonnet-5-5", rig.Controller.Settings.Model);
        Assert.Equal("claude-sonnet-5-5", rig.Saved[^1].Model);
        Assert.True(rig.Saved[^1].Autostart); // the rest of the settings are kept
        Assert.True(rig.Controller.Settings.HasApiKey);

        Assert.Equal("\"gpt-9\" isn't a model the mod knows. Options: opus, sonnet, haiku, fable, or a full model id.", rig.Post("/model gpt-9"));
        Assert.Equal("The model is Sonnet 5.5 already.", rig.Post("/model claude-sonnet-5-5"));
        Assert.Equal(saves + 1, rig.Saved.Length);

        Assert.Equal("Model set to Haiku 5.5. It applies from the next request.", rig.Post("/model claude-haiku-5-5-20261001"));
        Assert.Equal("claude-haiku-5-5-20261001", rig.Controller.Settings.Model);
        Assert.Single(rig.ApiBrains); // the brain plays on and picks it up
    }

    [Fact]
    public async Task Model_and_effort_say_what_claude_code_does_with_them()
    {
        using var rig = new Rig { Key = null };
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        Assert.Equal(BrainKind.Subscription, rig.Controller.Playing);

        Assert.Equal("Model set to Sonnet 5.5. Claude Code keeps its current model until its next session.", rig.Post("/model sonnet"));
        Assert.Equal("claude-sonnet-5-5", rig.Controller.Settings.Model);
        Assert.Equal("Effort set to max. Only the API brain uses it, so Claude Code playing now isn't affected.", rig.Post("/effort max"));
        Assert.Equal("max", rig.Controller.Settings.Effort);
    }

    [Fact]
    public async Task Effort_shows_sets_and_refuses_unknown_levels()
    {
        using var rig = new Rig();
        await rig.Init();

        Assert.Equal("The effort is high; high (the default) in elite rooms and high (the default) in boss rooms. " + Options, rig.Post("/effort"));
        Assert.Equal("Effort set to max. It applies from the next request." + BrainController.HighEffortCaution, rig.Post("/effort MAX"));
        Assert.Equal("max", rig.Controller.Settings.Effort);
        Assert.Equal("max", rig.Saved[^1].Effort);
        Assert.False(rig.Saved[^1].Autostart);
        Assert.Equal("\"extreme\" isn't an effort level. Options: low, medium, high, xhigh, max.", rig.Post("/effort extreme"));
        Assert.Equal("The effort is max already.", rig.Post("/effort max"));
        Assert.Single(rig.Saved);
    }

    private const string Options = "Options: low, medium, high, xhigh, max. /effort elite or /effort boss with a level, " +
                                   "same (the normal effort) or default (high, or the normal effort when higher) sets a fight's.";

    [Fact]
    public async Task Effort_shows_and_sets_the_elite_and_boss_efforts()
    {
        using var rig = new Rig();
        rig.Config = rig.Config with { Effort = "medium" };
        await rig.Init();

        Assert.Equal("The effort is medium; high (the default) in elite rooms and high (the default) in boss rooms. " + Options, rig.Post("/effort"));
        Assert.Equal("The effort in boss rooms is high (the default). " + Options, rig.Post("/effort boss"));

        Assert.Equal("The effort in boss rooms is now xhigh. It applies from the next request in a boss room." + BrainController.HighEffortCaution, rig.Post("/effort Boss XHIGH"));
        Assert.Equal("xhigh", rig.Controller.Settings.BossEffort);
        Assert.Equal("xhigh", rig.Saved[^1].BossEffort);
        Assert.Null(rig.Saved[^1].EliteEffort);
        Assert.Equal("medium", rig.Saved[^1].Effort); // the normal effort is untouched

        // A fight effort chosen below high is used as it is.
        Assert.Equal("The effort in elite rooms is now low. It applies from the next request in an elite room.", rig.Post("/effort elite low"));
        Assert.Equal("low", rig.Controller.Settings.EffortFor(BrainSettings.EliteRoom));
        Assert.Equal("The effort in elite rooms is now medium (the normal effort). It applies from the next request in an elite room.", rig.Post("/effort elite same"));
        Assert.Equal("same", rig.Saved[^1].EliteEffort);
        Assert.Equal("The effort in elite rooms is medium (the normal effort) already.", rig.Post("/effort elite same"));
        Assert.Equal("The effort is medium; medium (the normal effort) in elite rooms and xhigh in boss rooms. " + Options, rig.Post("/effort"));
        Assert.Equal("The effort in boss rooms is now high (the default). It applies from the next request in a boss room.", rig.Post("/effort boss default"));
        Assert.Null(rig.Saved[^1].BossEffort);
        Assert.Equal("\"loud\" isn't an effort level. " + Options, rig.Post("/effort boss loud"));

        // /effort with a level still sets the normal effort, and the default fight effort follows it up.
        Assert.Equal("Effort set to xhigh. It applies from the next request." + BrainController.HighEffortCaution, rig.Post("/effort xhigh"));
        Assert.Equal("xhigh", rig.Controller.Settings.EffortFor(BrainSettings.BossRoom));
        Assert.Contains(rig.Logged, l => l == "brain: boss effort set to xhigh by Mark");

        // Below high, the reply says the fight rooms keep their own.
        Assert.Equal("Effort set to low. It applies from the next request, except in boss rooms, which stay at high.", rig.Post("/effort low"));
        rig.Post("/effort elite default");
        Assert.Equal("Effort set to medium. It applies from the next request, except in elite and boss rooms, which stay at high.", rig.Post("/effort medium"));
        rig.Post("/effort boss max");
        Assert.Equal("Effort set to low. It applies from the next request, except in elite rooms (high) and boss rooms (max).", rig.Post("/effort low"));
    }

    [Fact]
    public async Task Budget_mentions_fight_efforts_that_differ()
    {
        using var rig = new Rig();
        rig.Config = rig.Config with { Effort = "medium", BossEffort = "max" };
        await rig.Init();

        Assert.Contains("Opus 5.5, medium effort, high in elite rooms and max in boss rooms.\n", rig.Post("/budget"));
        rig.Post("/effort boss default");
        Assert.Contains("Opus 5.5, medium effort, high in elite and boss rooms.\n", rig.Post("/budget"));
    }

    [Fact]
    public async Task Help_lists_the_brain_commands()
    {
        using var rig = new Rig();
        await rig.Init();

        var help = rig.Post("/help")!;

        Assert.Contains("/start lets the bot play by itself", help);
        Assert.Contains("/stop stops the bot until you /start it again", help);
        Assert.Contains("/budget shows the API spend and what's playing", help);
        Assert.Contains("/model [opus|sonnet|haiku|fable] shows or switches the model", help);
        Assert.Contains("/effort [low|medium|high|xhigh|max] or elite|boss [level|same|default] shows or sets the thinking effort", help);
    }

    [Theory]
    [InlineData("claude-opus-5-5", "Opus 5.5")]
    [InlineData("claude-sonnet-5-5", "Sonnet 5.5")]
    [InlineData("claude-haiku-5-5-20261001", "Haiku 5.5")]
    [InlineData("claude-fable-5-1", "Fable 5.1")]
    [InlineData("claude-opus-6", "Opus 6")]
    [InlineData("claude-opus-5-20261001", "Opus 5")]
    [InlineData("claude-opus-5-5[1m]", "Opus 5.5")]
    [InlineData("gpt-9", "gpt-9")]
    [InlineData("claude-3-5-sonnet", "claude-3-5-sonnet")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Model_names_read_like_people_say_them(string? id, string expected)
    {
        Assert.Equal(expected, BrainController.ModelName(id));
    }
}

/// <summary>A BrainController wired to fakes: brains that start and stop on command, a config
/// file and key in memory, a screen the test sets, and a poll loop that runs one round per
/// <see cref="Poll"/>.</summary>
file sealed class Rig : IDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _entered = new(0);
    private readonly SemaphoreSlim _release = new(0);
    private readonly ConcurrentQueue<string> _logged = new();
    private readonly List<FakeBrain> _api = [];
    private readonly List<FakeBrain> _sub = [];
    private readonly List<BrainSettings> _saved = [];
    private BrainSettings _config;
    private string? _key = BrainControllerTests.Key;
    private string? _screen = "menu";
    private BrainController? _controller;

    public Rig()
    {
        Directory.CreateDirectory(Dir);
        Spend = new SpendTracker(Dir);
        _config = new BrainSettings { ConfigDir = Dir, Effort = "high", MonthlyBudgetUsd = 200m };
    }

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "spiremind-controller-" + Guid.NewGuid().ToString("N"));
    public SpendTracker Spend { get; }
    public ChatHub Chat { get; } = new() { BotName = "Ironclad" };
    public DateTime Now { get; set; } = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    public int MaxRestartsPerRun { get; init; } = 3;
    public TimeSpan StopWaitLimit { get; init; } = TimeSpan.FromSeconds(10);
    public bool SaveThrows { get; init; }
    public volatile bool ScreenThrows;
    public volatile bool OtherClient;
    public TaskCompletionSource PollCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>What config.json holds (never a key).</summary>
    public BrainSettings Config
    {
        get { lock (_gate) return _config; }
        set { lock (_gate) _config = value; }
    }

    /// <summary>The key in the api_key file, or null.</summary>
    public string? Key
    {
        get { lock (_gate) return _key; }
        set { lock (_gate) _key = value; }
    }

    public string? Screen
    {
        get { lock (_gate) return _screen; }
        set { lock (_gate) _screen = value; }
    }

    public FakeBrain[] ApiBrains { get { lock (_gate) return _api.ToArray(); } }
    public FakeBrain[] SubBrains { get { lock (_gate) return _sub.ToArray(); } }
    public BrainSettings[] Saved { get { lock (_gate) return _saved.ToArray(); } }
    public string[] Logged => _logged.ToArray();

    public BrainController Controller => _controller ??= Build();

    /// <summary>Initialize, then wait for the poll loop's first round (it sees <see cref="Screen"/>).</summary>
    public async Task Init()
    {
        Controller.Initialize();
        Assert.True(await _entered.WaitAsync(Guard), "the poll loop didn't run");
    }

    /// <summary>Shows the poll loop a screen and waits until it has handled it.</summary>
    public async Task Poll(string? screen)
    {
        Screen = screen;
        _release.Release();
        Assert.True(await _entered.WaitAsync(Guard), "the poll loop didn't come back");
    }

    public string? Post(string text) => Chat.PostFromHuman("Mark", text).Reply;

    public long Mark() => Chat.LastSeq;

    public string[] LinesSince(long seq) => Chat.LinesSince(seq).Select(l => l.Text).ToArray();

    public static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("timed out waiting");
            await Task.Delay(5);
        }
    }

    private BrainController Build() => new(
        loadSettings: () => Config with { ApiKey = Key, ApiKeySource = Key is null ? ApiKeySource.None : ApiKeySource.File },
        saveSettings: s =>
        {
            if (SaveThrows) throw new IOException("disk full");
            lock (_gate) _saved.Add(s);
            Config = s with { ApiKey = null, ApiKeySource = ApiKeySource.None };
        },
        createApi: s => Add(_api, s),
        createSubscription: s => Add(_sub, s),
        currentScreen: _ => ScreenThrows ? throw new InvalidOperationException("the game isn't ready") : Task.FromResult(Screen),
        spend: Spend,
        chat: Chat,
        log: _logged.Enqueue,
        clock: () => Now,
        delay: async (_, ct) =>
        {
            _entered.Release();
            try
            {
                await _release.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                PollCancelled.TrySetResult();
                throw;
            }
        },
        otherClient: () => OtherClient)
    {
        MaxRestartsPerRun = MaxRestartsPerRun,
        StopWaitLimit = StopWaitLimit,
    };

    private FakeBrain Add(List<FakeBrain> list, BrainSettings settings)
    {
        var brain = new FakeBrain(settings);
        lock (_gate) list.Add(brain);
        return brain;
    }

    public void Dispose()
    {
        _controller?.Shutdown();
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}

/// <summary>A brain that plays until told otherwise. <see cref="Finish"/> stops it as the brain
/// would by itself, raising Stopped on the caller's thread.</summary>
file sealed class FakeBrain(BrainSettings settings) : IPlayBrain
{
    private readonly object _gate = new();
    private bool _running;
    private int _starts;
    private int _stopCalls;
    private int _aborts;

    /// <summary>The settings the factory was called with.</summary>
    public BrainSettings Settings => settings;

    public int Starts { get { lock (_gate) return _starts; } }
    public int StopCalls { get { lock (_gate) return _stopCalls; } }
    public int Aborts { get { lock (_gate) return _aborts; } }
    public TaskCompletionSource StopCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>When set, StopAsync blocks its caller's thread until it is set.</summary>
    public ManualResetEventSlim? BlockStop { get; set; }

    /// <summary>When set, StopAsync waits for it (without blocking) before the brain stops.</summary>
    public TaskCompletionSource? HoldStop { get; set; }

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    /// <summary>What the brain says it saw last; tests set it before <see cref="Finish"/>.</summary>
    public bool? LastSeenInRun { get; set; }

    public event Action<BrainStopReason, string>? Stopped;

    public bool Start()
    {
        lock (_gate)
        {
            if (_running) return false;
            _running = true;
            _starts++;
            return true;
        }
    }

    public async Task StopAsync()
    {
        lock (_gate) _stopCalls++;
        StopCalled.TrySetResult();
        BlockStop?.Wait(TimeSpan.FromSeconds(10));
        if (HoldStop is { } hold)
            await hold.Task;
        Finish(BrainStopReason.UserStop, "stopped");
    }

    public void Abort()
    {
        lock (_gate) _aborts++;
        Finish(BrainStopReason.UserStop, "aborted");
    }

    public void Finish(BrainStopReason reason, string message = "It stopped.")
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
        }
        Stopped?.Invoke(reason, message);
    }
}
