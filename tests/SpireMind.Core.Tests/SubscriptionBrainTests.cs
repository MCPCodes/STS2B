using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class SubscriptionBrainTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    // ---- Launching ----

    [Fact]
    public async Task Launches_claude_headless_with_the_exact_flags_in_the_repo()
    {
        using var rig = new SubRig();

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        var auth = rig.Launcher.Specs[0];
        Assert.Equal(rig.Claude, auth.FileName);
        Assert.Equal(["auth", "status"], auth.Arguments);
        Assert.Equal(rig.Repo, auth.WorkingDirectory);

        var spec = Assert.Single(rig.Launcher.Sessions);
        Assert.Equal(rig.Claude, spec.FileName);
        Assert.Equal(rig.Repo, spec.WorkingDirectory);
        var id = spec.Arguments[3];
        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(
        [
            "-p", "/play the whole run",
            "--session-id", id,
            "--strict-mcp-config",
            "--mcp-config", "{\"mcpServers\":{\"spiremind\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:8765/mcp\"}}}",
            "--tools", "Read,Edit,Write,ToolSearch",
            "--permission-mode", "dontAsk",
            "--allowedTools", "mcp__spiremind", "mcp__spiremind__*", $"Edit(/{rig.Repo}/runs/**)", $"Edit(/{rig.Repo}/strategy/**)",
            "--output-format", "stream-json",
            "--verbose",
        ], spec.Arguments);
        Assert.StartsWith("Edit(//", spec.Arguments[14]);
        Assert.DoesNotContain("///", spec.Arguments[14]);
    }

    [Fact]
    public async Task The_child_environment_drops_api_credentials_and_gets_a_full_path()
    {
        using var rig = new SubRig();

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        var env = rig.Launcher.Sessions[0].EnvironmentOverrides;
        foreach (var name in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDECODE" })
        {
            Assert.True(env.ContainsKey(name), name);
            Assert.Null(env[name]);
        }
        var sep = Path.PathSeparator;
        Assert.Equal($"{Path.GetDirectoryName(rig.Claude)}{sep}/opt/homebrew/bin{sep}/usr/local/bin{sep}/usr/bin{sep}/bin{sep}/custom/bin", env["PATH"]);
        Assert.Equal(7, env.Count);
        Assert.False(env.ContainsKey("HOME"));
        Assert.False(env.ContainsKey("SPIREMIND_KEEP"));

        // The auth check runs under the same rules.
        Assert.Equal(env.OrderBy(e => e.Key), rig.Launcher.Specs[0].EnvironmentOverrides.OrderBy(e => e.Key));
        Assert.DoesNotContain(rig.Logged, l => l.Contains(SubRig.FakeKey));
    }

    [Fact]
    public async Task A_trailing_slash_on_the_repo_changes_nothing()
    {
        using var rig = new SubRig();
        rig.Options = rig.Options with { WorkingDirectory = rig.Repo + "/" };

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        var spec = rig.Launcher.Sessions[0];
        Assert.Equal(rig.Repo, spec.WorkingDirectory);
        Assert.Contains($"Edit(/{rig.Repo}/runs/**)", spec.Arguments);
    }

    [Fact]
    public async Task The_model_is_passed_when_set()
    {
        using var rig = new SubRig();
        rig.Options = rig.Options with { Model = () => "claude-sonnet-5-5" };

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        var arguments = rig.Launcher.Sessions[0].Arguments;
        Assert.Equal(["--verbose", "--model", "claude-sonnet-5-5"], arguments.TakeLast(3));
    }

    [Fact]
    public async Task A_new_model_reaches_the_next_session()
    {
        using var rig = new SubRig();
        var model = "claude-opus-5-5";
        rig.Options = rig.Options with { Model = () => model };
        rig.Launcher.Then(c =>
        {
            model = "claude-sonnet-5-5"; // /model while the first session plays
            c.Exit(0);
        });

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        var sessions = rig.Launcher.Sessions;
        Assert.Equal(2, sessions.Length);
        Assert.Equal(["--model", "claude-opus-5-5"], sessions[0].Arguments.TakeLast(2));
        Assert.Equal("--resume", sessions[1].Arguments[2]);
        Assert.Equal(["--model", "claude-sonnet-5-5"], sessions[1].Arguments.TakeLast(2));
    }

    [Fact]
    public void Default_search_paths_cover_the_usual_installs_in_order()
    {
        Assert.Equal(
            ["/opt/homebrew/bin/claude", "/home/mark/.local/bin/claude", "/usr/local/bin/claude", "/home/mark/.claude/local/claude"],
            SubscriptionBrain.DefaultSearchPaths("/home/mark"));
    }

    [Fact]
    public async Task A_configured_claude_path_wins()
    {
        using var rig = new SubRig();
        var custom = rig.WriteFile("home/bin/claude", "#!/bin/sh\n");
        rig.Options = rig.Options with { ClaudePath = "~/bin/claude" };

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        Assert.All(rig.Launcher.Specs, s => Assert.Equal(custom, s.FileName));
        Assert.StartsWith(Path.GetDirectoryName(custom)!, rig.Launcher.Sessions[0].EnvironmentOverrides["PATH"]);
    }

    [Fact]
    public async Task A_missing_claude_path_falls_back_to_the_search()
    {
        using var rig = new SubRig();
        rig.Options = rig.Options with { ClaudePath = Path.Combine(rig.Root, "gone", "claude") };

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        Assert.Equal(rig.Claude, rig.Launcher.Sessions[0].FileName);
        Assert.Contains(rig.Logged, l => l.Contains("doesn't exist"));
    }

    // ---- Setup problems ----

    [Fact]
    public async Task No_binary_stops_with_error_and_says_how_to_install()
    {
        using var rig = new SubRig();
        rig.Options = rig.Options with { SearchPaths = [Path.Combine(rig.Root, "nope", "claude")] };

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(SubscriptionBrain.NoClaude, message);
        Assert.Contains("brew install --cask claude-code", message);
        Assert.Contains("claude_path", message);
        Assert.Equal([message], rig.Announced);
        Assert.Empty(rig.Launcher.Specs);
        Assert.False(rig.Brain.IsRunning);
    }

    [Theory]
    [InlineData("no play.md")]
    [InlineData("no folder")]
    [InlineData("not configured")]
    public async Task No_repo_stops_with_error_and_says_where_to_set_it(string problem)
    {
        using var rig = new SubRig();
        switch (problem)
        {
            case "no play.md":
                File.Delete(Path.Combine(rig.Repo, ".claude", "commands", "play.md"));
                break;
            case "no folder":
                rig.Options = rig.Options with { WorkingDirectory = Path.Combine(rig.Root, "elsewhere") };
                break;
            default:
                rig.Options = rig.Options with { WorkingDirectory = null };
                break;
        }

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(SubscriptionBrain.NoRepo, message);
        Assert.Contains("\"repo\" in ~/.config/spiremind/config.json", message);
        Assert.Equal([message], rig.Announced);
        Assert.Empty(rig.Launcher.Specs);
    }

    [Theory]
    [InlineData("exit code")]
    [InlineData("logged out")]
    [InlineData("not json")]
    [InlineData("timeout")]
    public async Task Auth_failures_stop_before_any_session(string problem)
    {
        using var rig = new SubRig();
        rig.Launcher.Auth = problem switch
        {
            "exit code" => c => { c.Out(SubRig.AuthJson(true)); c.Exit(1); },
            "logged out" => c => { c.Out(SubRig.AuthJson(false)); c.Exit(0); },
            "not json" => c => { c.Out("Not logged in. Run claude to log in."); c.Exit(0); },
            _ => _ => { },
        };

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(SubscriptionBrain.NotSignedIn, message);
        Assert.Contains("Run claude once in Terminal", message);
        Assert.Equal([message], rig.Announced);
        Assert.Empty(rig.Launcher.Sessions);
        Assert.Equal(0, rig.InRunCalls);
        var auth = Assert.Single(rig.Launcher.Children);
        Assert.True(auth.Exited.IsCompleted);
        Assert.True(auth.Disposed);
        if (problem == "timeout")
        {
            Assert.Contains("kill", auth.Events);
            Assert.Contains(TimeSpan.FromSeconds(15), rig.Delays);
        }
        Assert.DoesNotContain(rig.Logged, l => l.Contains(SubRig.Email));
    }

    [Fact]
    public async Task Auth_status_logs_only_the_method_and_the_plan()
    {
        using var rig = new SubRig();
        rig.Launcher.Auth = c =>
        {
            // Pretty-printed, with a line before it.
            c.Out("Checking...");
            foreach (var line in JsonNode.Parse(SubRig.AuthJson(true))!.ToJsonString(new() { WriteIndented = true }).Split('\n'))
                c.Out(line);
            c.Exit(0);
        };

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        var line = Assert.Single(rig.Logged, l => l.Contains("auth status"));
        Assert.Contains("loggedIn true", line);
        Assert.Contains("authMethod claude.ai", line);
        Assert.Contains("subscriptionType max", line);
        Assert.DoesNotContain(rig.Logged, l => l.Contains(SubRig.Email) || l.Contains("Checking") || l.Contains(SubRig.OrgName));
    }

    [Fact]
    public async Task A_launch_failure_stops_with_error()
    {
        using var rig = new SubRig();
        rig.Launcher.Throws = new System.ComponentModel.Win32Exception(13, "Permission denied");

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(SubscriptionBrain.CantStart, message);
        Assert.Equal([message], rig.Announced);
        Assert.Contains(rig.Logged, l => l.Contains("Permission denied"));
    }

    // ---- Waiting for a run ----

    [Fact]
    public async Task Waits_for_a_run_before_launching()
    {
        using var rig = new SubRig();
        rig.InRunAnswers = [null, false, null, true];

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        Assert.Equal(4, rig.InRunCalls);
        Assert.All(rig.LaunchesBeforeInRun.Take(4), n => Assert.Equal(1, n));
        Assert.Equal(3, rig.Delays.Count(d => d == TimeSpan.FromSeconds(5)));
        Assert.Single(rig.Launcher.Sessions);
    }

    [Fact]
    public async Task An_inRun_that_throws_counts_as_unknown()
    {
        using var rig = new SubRig();
        var calls = 0;
        rig.InRun = _ => ++calls == 1 ? throw new InvalidOperationException("game busy") : Task.FromResult<bool?>(true);

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        Assert.Equal(2, calls);
        Assert.Contains(rig.Logged, l => l.Contains("game busy"));
    }

    [Fact]
    public async Task Stop_while_waiting_for_a_run_launches_nothing()
    {
        using var rig = new SubRig { InRunDefault = false };
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.InRun = _ =>
        {
            polled.TrySetResult();
            return Task.FromResult<bool?>(false);
        };

        rig.Brain.Start();
        await polled.Task.WaitAsync(Guard);
        await rig.Brain.StopAsync().WaitAsync(Guard);

        Assert.Equal(BrainStopReason.UserStop, (await rig.Stopped.Task.WaitAsync(Guard)).Reason);
        Assert.Empty(rig.Launcher.Sessions);
        Assert.Empty(rig.Announced);
    }

    // ---- Sessions ending ----

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task A_session_that_ends_mid_run_is_resumed_with_the_same_flags(bool? stillInRun)
    {
        using var rig = new SubRig();
        rig.Options = rig.Options with { Model = () => "claude-opus-5-5" };
        rig.InRunAnswers = [true, stillInRun];
        rig.Launcher.Then(c => c.Exit(0));

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        var sessions = rig.Launcher.Sessions;
        Assert.Equal(2, sessions.Length);
        var expected = sessions[0].Arguments.ToArray();
        expected[1] = SubscriptionBrain.ResumePrompt;
        expected[2] = "--resume";
        Assert.Equal(expected, sessions[1].Arguments);
        Assert.Equal(sessions[0].WorkingDirectory, sessions[1].WorkingDirectory);
        Assert.Same(sessions[0].EnvironmentOverrides, sessions[1].EnvironmentOverrides);
        Assert.Empty(rig.Announced);
        Assert.True(rig.Launcher.Children[1].Disposed);
    }

    [Fact]
    public async Task A_session_that_ends_outside_a_run_stops_with_RunEnded()
    {
        using var rig = new SubRig();
        rig.InRunAnswers = [true, false];
        rig.Launcher.Then(c => c.Exit(0));

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.RunEnded, reason);
        Assert.Equal(SubscriptionBrain.RunOver, message);
        Assert.Empty(rig.Announced);
        Assert.Single(rig.Launcher.Sessions);
        Assert.All(rig.Launcher.Children, c => Assert.True(c.Disposed));
        Assert.False(rig.StoppedWhileRunning);
    }

    [Fact]
    public async Task A_lost_conversation_starts_fresh_with_the_original_prompt()
    {
        using var rig = new SubRig();
        rig.Launcher
            .Then(c => c.Exit(0))
            .Then(c =>
            {
                c.Err("No conversation found with session ID: 0f8e2c1a-1111-2222-3333-444455556666");
                c.Exit(1);
            });

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        var sessions = rig.Launcher.Sessions;
        Assert.Equal(3, sessions.Length);
        var first = sessions[0].Arguments[3];
        Assert.Equal(["-p", SubscriptionBrain.ResumePrompt, "--resume", first], sessions[1].Arguments.Take(4));
        Assert.Equal("/play the whole run", sessions[2].Arguments[1]);
        Assert.Equal("--session-id", sessions[2].Arguments[2]);
        Assert.NotEqual(first, sessions[2].Arguments[3]);
        Assert.True(Guid.TryParse(sessions[2].Arguments[3], out _));
        Assert.Equal(sessions[0].Arguments.Skip(4), sessions[2].Arguments.Skip(4));
    }

    [Fact]
    public async Task Quick_exits_in_a_row_stop_with_error()
    {
        using var rig = new SubRig();
        for (var i = 0; i < 5; i++)
            rig.Launcher.Then(c => c.Exit(1));

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(SubscriptionBrain.KeepsExiting, message);
        Assert.Equal([message], rig.Announced);
        Assert.Equal(3, rig.Launcher.Sessions.Length);
    }

    [Fact]
    public async Task A_long_session_resets_the_quick_exit_count()
    {
        using var rig = new SubRig();
        rig.Launcher
            .Then(c => c.Exit(0))
            .Then(c =>
            {
                rig.Now += TimeSpan.FromMinutes(3);
                c.Exit(0);
            })
            .Then(c => c.Exit(0))
            .Then(c => c.Exit(0));

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        Assert.Equal(5, rig.Launcher.Sessions.Length);
        Assert.True(rig.Brain.IsRunning);
        Assert.Empty(rig.Announced);
    }

    // ---- Output ----

    [Fact]
    public async Task Stream_json_is_logged_and_other_lines_are_tolerated()
    {
        using var rig = new SubRig();
        rig.InRunAnswers = [true, false];
        var longText = new string('x', 1000);
        rig.Launcher.Then(c =>
        {
            c.Out("Some banner that isn't JSON");
            c.Out("{not json either");
            c.Out(Json(new JsonObject
            {
                ["type"] = "system",
                ["subtype"] = "init",
                ["model"] = "claude-opus-5-5",
                ["mcp_servers"] = new JsonArray(new JsonObject { ["name"] = "spiremind", ["status"] = "failed" }),
            }));
            c.Out(Assistant(Text(longText), ToolUse("mcp__spiremind__act")));
            c.Out(Json(new JsonObject
            {
                ["type"] = "user",
                ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["content"] = "SECRET-GAME-TEXT" }) },
            }));
            c.Out(Result("success", false, "Run over.", turns: 42, durationMs: 81234));
            c.Err("a warning on stderr");
            c.Exit(0);
        });

        rig.Brain.Start();
        var (reason, _) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.RunEnded, reason);
        var logged = rig.Logged;
        Assert.Contains(logged, l => l.Contains("Some banner that isn't JSON"));
        Assert.Contains(logged, l => l.Contains("{not json either"));
        Assert.Contains(logged, l => l.Contains("claude-opus-5-5") && l.Contains("spiremind failed"));
        Assert.Contains(logged, l => l.Contains("warning: the spiremind MCP server is failed"));
        var said = Assert.Single(logged, l => l.Contains("claude says:"));
        Assert.Contains("xxxx...", said);
        Assert.True(said.Length < 400);
        Assert.Contains(logged, l => l.EndsWith("claude calls act"));
        Assert.Contains(logged, l => l.Contains("result success, is_error false, 42 turns, 81.2 s: Run over."));
        Assert.Contains(logged, l => l.Contains("claude stderr: a warning on stderr"));
        Assert.DoesNotContain(logged, l => l.Contains("SECRET-GAME-TEXT"));
        Assert.Empty(rig.Announced);
    }

    [Fact]
    public async Task A_connected_mcp_server_logs_no_warning()
    {
        using var rig = new SubRig();
        rig.Launcher.Then(c =>
        {
            c.Out(Json(new JsonObject
            {
                ["type"] = "system",
                ["subtype"] = "init",
                ["model"] = "claude-opus-5-5",
                ["mcp_servers"] = new JsonArray(new JsonObject { ["name"] = "spiremind", ["status"] = "connected" }),
            }));
            c.Exit(0);
        });

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        Assert.Contains(rig.Logged, l => l.Contains("spiremind connected"));
        Assert.DoesNotContain(rig.Logged, l => l.Contains("warning"));
    }

    // ---- Usage limits ----

    [Fact]
    public async Task A_usage_limit_in_the_result_stops_with_one_line_and_the_reset_time()
    {
        using var rig = new SubRig();
        var reset = new DateTimeOffset(rig.Now).AddHours(3);
        rig.Launcher.Then(c =>
        {
            c.Out(Assistant(Text($"Claude AI usage limit reached|{reset.ToUnixTimeSeconds()}")));
            c.Out(Result("success", true, $"Claude AI usage limit reached|{reset.ToUnixTimeSeconds()}"));
            c.Exit(1);
        });

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.StartsWith(SubscriptionBrain.UsageLimit, message);
        Assert.Contains("resets", message);
        Assert.Contains(reset.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture), message);
        Assert.DoesNotContain("|", message);
        Assert.Equal([message], rig.Announced);
        Assert.Single(rig.Launcher.Sessions);
        Assert.Equal(1, rig.InRunCalls);
    }

    [Fact]
    public async Task A_usage_limit_on_stderr_relays_the_sentence_with_the_reset()
    {
        using var rig = new SubRig();
        rig.Launcher.Then(c =>
        {
            c.Err("Error: You've hit your limit · resets 5pm (Europe/London). Upgrade for more.");
            c.Exit(1);
        });

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal($"{SubscriptionBrain.UsageLimit} Claude Code says: \"Error: You've hit your limit · resets 5pm (Europe/London).\"", message);
        Assert.Equal([message], rig.Announced);
    }

    [Fact]
    public async Task A_usage_limit_without_a_reset_time_is_just_the_line()
    {
        using var rig = new SubRig();
        rig.Launcher.Then(c =>
        {
            c.Out(Assistant(Text("You're out of extra usage.")));
            c.Exit(1);
        });

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(SubscriptionBrain.UsageLimit, message);
        Assert.Equal([message], rig.Announced);
    }

    [Fact]
    public async Task Limit_words_in_game_text_or_a_passing_rate_limit_do_not_stop_it()
    {
        using var rig = new SubRig();
        rig.Launcher.Then(c =>
        {
            c.Out(Json(new JsonObject
            {
                ["type"] = "user",
                ["message"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["content"] = "Hand limit reached. Usage limit reached for Bottled Flame." }) },
            }));
            c.Err("API rate limit hit, retrying in 5 seconds");
            c.Out(Assistant(ToolUse("mcp__spiremind__wait_for_decision")));
            c.Out(Result("success", false, "Stopping here."));
            c.Exit(0);
        });

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);

        Assert.Equal(2, rig.Launcher.Sessions.Length);
        Assert.Empty(rig.Announced);
        Assert.True(rig.Brain.IsRunning);
    }

    // ---- Stopping ----

    [Fact]
    public async Task Stop_interrupts_then_kills_after_the_grace()
    {
        using var rig = new SubRig();
        rig.Launcher.IgnoreInterrupt = true;

        rig.Brain.Start();
        var child = await rig.Launcher.Running.Task.WaitAsync(Guard);
        await rig.Brain.StopAsync().WaitAsync(Guard);

        Assert.Equal(["interrupt", "kill", "dispose"], child.Events);
        Assert.Equal(1, rig.Delays.Count(d => d == TimeSpan.FromSeconds(5)));
        Assert.Contains("subscription: killing Claude Code (pid 1001)", rig.Logged);
        Assert.DoesNotContain(rig.Logged, l => l.Contains("after the stop") || l.Contains("SIGINT"));
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);
        Assert.Equal(BrainStopReason.UserStop, reason);
        Assert.Equal(SubscriptionBrain.Stopping, message);
        Assert.Empty(rig.Announced);
        Assert.False(rig.Brain.IsRunning);
        Assert.False(rig.StoppedWhileRunning);
    }

    [Fact]
    public async Task Stop_lets_a_child_that_quits_on_interrupt_go_without_a_kill()
    {
        using var rig = new SubRig();

        rig.Brain.Start();
        var child = await rig.Launcher.Running.Task.WaitAsync(Guard);
        await rig.Brain.StopAsync().WaitAsync(Guard);

        Assert.Equal(["interrupt", "dispose"], child.Events);
        Assert.Equal(BrainStopReason.UserStop, (await rig.Stopped.Task.WaitAsync(Guard)).Reason);
        Assert.Single(rig.Launcher.Sessions);
        Assert.Contains("subscription: Claude Code (pid 1001) exited with code 130 after the stop", rig.Logged);
        Assert.DoesNotContain(rig.Logged, l => l.Contains("SIGINT") || l.Contains("killing"));
    }

    [Fact]
    public async Task Stop_says_so_when_sigint_could_not_be_sent()
    {
        using var rig = new SubRig();
        rig.Launcher.NoSigInt = true;

        rig.Brain.Start();
        var child = await rig.Launcher.Running.Task.WaitAsync(Guard);
        await rig.Brain.StopAsync().WaitAsync(Guard);

        Assert.Equal(["interrupt", "kill", "dispose"], child.Events);
        var logged = rig.Logged;
        var asked = Array.IndexOf(logged, "subscription: asking Claude Code (pid 1001) to stop");
        Assert.True(asked >= 0);
        Assert.Equal("subscription: couldn't send SIGINT to Claude Code (pid 1001), so it was killed instead", logged[asked + 1]);
        Assert.Equal("subscription: Claude Code (pid 1001) exited with code 137 after the stop", logged[asked + 2]);
        Assert.DoesNotContain(logged, l => l.Contains("killing"));
        Assert.Equal(BrainStopReason.UserStop, (await rig.Stopped.Task.WaitAsync(Guard)).Reason);
    }

    [Fact]
    public async Task Abort_kills_at_once()
    {
        using var rig = new SubRig();
        rig.Launcher.IgnoreInterrupt = true;

        rig.Brain.Start();
        var child = await rig.Launcher.Running.Task.WaitAsync(Guard);
        rig.Brain.Abort();

        Assert.Equal(BrainStopReason.UserStop, (await rig.Stopped.Task.WaitAsync(Guard)).Reason);
        Assert.Contains("kill", child.Events);
        Assert.DoesNotContain("interrupt", child.Events);
        Assert.True(child.Disposed);
        Assert.Empty(rig.Announced);
        Assert.Single(rig.Launcher.Sessions);
    }

    [Fact]
    public async Task Abort_during_the_auth_check_kills_it()
    {
        using var rig = new SubRig();
        var started = new TaskCompletionSource<FakeChild>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Launcher.Auth = c => started.TrySetResult(c);
        rig.Delay = (_, ct) => Task.Delay(Timeout.Infinite, ct);

        rig.Brain.Start();
        var auth = await started.Task.WaitAsync(Guard);
        rig.Brain.Abort();

        Assert.Equal(BrainStopReason.UserStop, (await rig.Stopped.Task.WaitAsync(Guard)).Reason);
        Assert.Contains("kill", auth.Events);
        Assert.True(auth.Disposed);
        Assert.Empty(rig.Launcher.Sessions);
        Assert.Empty(rig.Announced);
    }

    [Fact]
    public async Task Dispose_aborts()
    {
        using var rig = new SubRig();

        rig.Brain.Start();
        var child = await rig.Launcher.Running.Task.WaitAsync(Guard);
        rig.Brain.Dispose();

        Assert.Equal(BrainStopReason.UserStop, (await rig.Stopped.Task.WaitAsync(Guard)).Reason);
        Assert.Contains("kill", child.Events);
        Assert.True(child.Disposed);
    }

    [Fact]
    public async Task Stopped_is_raised_exactly_once_and_a_handler_may_stop_again()
    {
        using var rig = new SubRig();
        Task? fromHandler = null;
        rig.OnStopped = brain => fromHandler = brain.StopAsync();

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);
        Assert.False(rig.Brain.Start());
        await rig.Brain.StopAsync().WaitAsync(Guard);
        await rig.Brain.StopAsync().WaitAsync(Guard);
        rig.Brain.Abort();

        await rig.Stopped.Task.WaitAsync(Guard);
        Assert.Equal(1, rig.StoppedCount);
        Assert.NotNull(fromHandler);
        Assert.True(fromHandler!.IsCompleted);
    }

    [Fact]
    public async Task Callbacks_that_throw_do_not_break_the_loop()
    {
        using var rig = new SubRig { AnnounceThrows = true, LogThrows = true };
        rig.Launcher.Then(c =>
        {
            c.Out(Assistant(Text("Playing.")));
            c.Err("noise");
            c.Exit(0);
        });

        rig.Brain.Start();
        await rig.Launcher.Running.Task.WaitAsync(Guard);
        Assert.Equal(2, rig.Launcher.Sessions.Length);
        await rig.Brain.StopAsync().WaitAsync(Guard);

        Assert.Equal(BrainStopReason.UserStop, (await rig.Stopped.Task.WaitAsync(Guard)).Reason);
        Assert.All(rig.Launcher.Children, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task An_announce_that_throws_still_ends_the_loop_cleanly()
    {
        using var rig = new SubRig { AnnounceThrows = true, LogThrows = true };
        rig.Options = rig.Options with { SearchPaths = [] };

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(SubscriptionBrain.NoClaude, message);
        Assert.Equal([message], rig.Announced);
        Assert.False(rig.Brain.IsRunning);
        await rig.Brain.StopAsync().WaitAsync(Guard);
        Assert.True(rig.Brain.Start());
    }

    // ---- Helpers ----

    private static string Json(JsonObject json) => json.ToJsonString();

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

    private static JsonObject ToolUse(string name) => new() { ["type"] = "tool_use", ["id"] = "toolu_1", ["name"] = name, ["input"] = new JsonObject() };

    private static string Assistant(params JsonObject[] blocks) => Json(new JsonObject
    {
        ["type"] = "assistant",
        ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(blocks.Cast<JsonNode>().ToArray()) },
    });

    private static string Result(string subtype, bool isError, string text, int turns = 3, int durationMs = 1500) => Json(new JsonObject
    {
        ["type"] = "result",
        ["subtype"] = subtype,
        ["is_error"] = isError,
        ["num_turns"] = turns,
        ["duration_ms"] = durationMs,
        ["result"] = text,
    });
}

/// <summary>A SubscriptionBrain wired to fakes: a temp repo and home with a claude binary, a
/// scripted launcher, scripted inRun answers, a settable clock and delays that return at once.</summary>
file sealed class SubRig : IDisposable
{
    public const string FakeKey = "sk-ant-api03-must-never-show";
    public const string Email = "mark@example.com";
    public const string OrgName = "Mark's Org";

    private readonly ConcurrentQueue<string> _announced = new();
    private readonly ConcurrentQueue<string> _logged = new();
    private readonly ConcurrentQueue<TimeSpan> _delays = new();
    private readonly ConcurrentQueue<int> _launchesBeforeInRun = new();
    private readonly ConcurrentQueue<bool?> _answers = new();
    private SubscriptionBrain? _brain;
    private int _inRunCalls;
    private int _stoppedCount;

    public SubRig()
    {
        WriteFile("repo/.claude/commands/play.md", "Play the game.\n");
        Claude = WriteFile("home/.local/bin/claude", "#!/bin/sh\n");
        Options = new SubscriptionBrainOptions
        {
            Port = 8765,
            WorkingDirectory = Repo,
            HomeDirectory = Path.Combine(Root, "home"),
            SearchPaths = [Path.Combine(Root, "opt", "claude"), Claude],
            InheritedEnvironment = new Dictionary<string, string>
            {
                ["PATH"] = $"/usr/bin{Path.PathSeparator}/custom/bin{Path.PathSeparator}/bin",
                ["HOME"] = Path.Combine(Root, "home"),
                ["ANTHROPIC_API_KEY"] = FakeKey,
                ["ANTHROPIC_AUTH_TOKEN"] = "token",
                ["CLAUDE_CODE_USE_BEDROCK"] = "1",
                ["CLAUDE_CODE_USE_VERTEX"] = "1",
                ["SPIREMIND_KEEP"] = "yes",
            },
        };
    }

    public static string AuthJson(bool loggedIn) => new JsonObject
    {
        ["loggedIn"] = loggedIn,
        ["authMethod"] = "claude.ai",
        ["apiProvider"] = "firstParty",
        ["email"] = Email,
        ["orgName"] = OrgName,
        ["subscriptionType"] = "max",
    }.ToJsonString();

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "spiremind-sub-" + Guid.NewGuid().ToString("N"));
    public string Repo => Path.Combine(Root, "repo");
    public string Claude { get; }
    public SubscriptionBrainOptions Options { get; set; }
    public FakeLauncher Launcher { get; } = new();
    public DateTime Now { get; set; } = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    public bool AnnounceThrows { get; init; }
    public bool LogThrows { get; init; }
    public bool? InRunDefault { get; init; } = true;
    public Func<CancellationToken, Task<bool?>>? InRun { get; set; }
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }
    public Action<SubscriptionBrain>? OnStopped { get; set; }
    public TaskCompletionSource<(BrainStopReason Reason, string Message)> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool StoppedWhileRunning { get; private set; }

    /// <summary>inRun's answers in order; once they run out it answers <see cref="InRunDefault"/>.</summary>
    public IEnumerable<bool?> InRunAnswers
    {
        set
        {
            foreach (var answer in value)
                _answers.Enqueue(answer);
        }
    }

    public int InRunCalls => Volatile.Read(ref _inRunCalls);
    public int StoppedCount => Volatile.Read(ref _stoppedCount);
    public int[] LaunchesBeforeInRun => _launchesBeforeInRun.ToArray();
    public string[] Announced => _announced.ToArray();
    public string[] Logged => _logged.ToArray();
    public TimeSpan[] Delays => _delays.ToArray();

    public SubscriptionBrain Brain => _brain ??= Build();

    public string WriteFile(string relative, string text)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private SubscriptionBrain Build()
    {
        var brain = new SubscriptionBrain(Options, Launcher,
            inRun: ct =>
            {
                Interlocked.Increment(ref _inRunCalls);
                _launchesBeforeInRun.Enqueue(Launcher.Specs.Length);
                if (InRun is { } inRun)
                    return inRun(ct);
                return Task.FromResult(_answers.TryDequeue(out var answer) ? answer : InRunDefault);
            },
            announce: line =>
            {
                _announced.Enqueue(line);
                if (AnnounceThrows) throw new InvalidOperationException("Too many chat lines");
            },
            log: line =>
            {
                _logged.Enqueue(line);
                if (LogThrows) throw new IOException("log is gone");
            },
            clock: () => Now,
            delay: Delay ?? (async (t, ct) =>
            {
                _delays.Enqueue(t);
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            }));
        brain.Stopped += (reason, message) =>
        {
            Interlocked.Increment(ref _stoppedCount);
            StoppedWhileRunning = brain.IsRunning;
            OnStopped?.Invoke(brain);
            Stopped.TrySetResult((reason, message));
        };
        return brain;
    }

    public void Dispose()
    {
        try { _brain?.StopAsync().Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception) { /* the test already failed */ }
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}

/// <summary>Scripted processes. "claude auth status" runs <see cref="Auth"/>; sessions run the
/// scripts given to <see cref="Then"/> in order, and once those run out a session stays running
/// and completes <see cref="Running"/>.</summary>
file sealed class FakeLauncher : IProcessLauncher
{
    private readonly object _gate = new();
    private readonly List<(ChildProcessSpec Spec, FakeChild Child)> _started = [];
    private readonly Queue<Action<FakeChild>> _scripts = new();

    public Action<FakeChild> Auth { get; set; } = c =>
    {
        c.Out(SubRig.AuthJson(true));
        c.Exit(0);
    };

    public Exception? Throws { get; set; }
    public bool IgnoreInterrupt { get; set; }
    public bool NoSigInt { get; set; }
    public TaskCompletionSource<FakeChild> Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ChildProcessSpec[] Specs
    {
        get { lock (_gate) return _started.Select(s => s.Spec).ToArray(); }
    }

    public ChildProcessSpec[] Sessions
    {
        get { lock (_gate) return _started.Where(s => !IsAuth(s.Spec)).Select(s => s.Spec).ToArray(); }
    }

    public FakeChild[] Children
    {
        get { lock (_gate) return _started.Select(s => s.Child).ToArray(); }
    }

    public FakeLauncher Then(Action<FakeChild> script)
    {
        lock (_gate) _scripts.Enqueue(script);
        return this;
    }

    public IChildProcess Start(ChildProcessSpec spec)
    {
        if (Throws is { } error)
            throw error;
        Action<FakeChild>? script;
        FakeChild child;
        lock (_gate)
        {
            child = new FakeChild(1000 + _started.Count) { ExitOnInterrupt = !IgnoreInterrupt, KillOnInterrupt = NoSigInt };
            _started.Add((spec, child));
            script = IsAuth(spec) ? Auth : _scripts.Count > 0 ? _scripts.Dequeue() : null;
        }
        if (script is null)
            Running.TrySetResult(child);
        else
            script(child);
        return child;
    }

    private static bool IsAuth(ChildProcessSpec spec) => spec.Arguments is ["auth", "status"];
}

file sealed class FakeChild(int id) : IChildProcess
{
    private readonly Channel<string> _out = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _err = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<string> _events = new();
    private int _disposed;

    public bool ExitOnInterrupt { get; init; } = true;

    /// <summary>Interrupt kills instead, as ChildProcess does when SIGINT can't be sent.</summary>
    public bool KillOnInterrupt { get; init; }
    public int Id => id;
    public ChannelReader<string> StandardOutput => _out.Reader;
    public ChannelReader<string> StandardError => _err.Reader;
    public Task<int> Exited => _exited.Task;
    public string[] Events => _events.ToArray();
    public bool Disposed => Volatile.Read(ref _disposed) != 0;

    public void Out(string line) => _out.Writer.TryWrite(line);

    public void Err(string line) => _err.Writer.TryWrite(line);

    public void Exit(int code)
    {
        _out.Writer.TryComplete();
        _err.Writer.TryComplete();
        _exited.TrySetResult(code);
    }

    public bool Interrupt()
    {
        _events.Enqueue("interrupt");
        if (KillOnInterrupt)
        {
            Kill();
            return false;
        }
        if (ExitOnInterrupt)
            Exit(130);
        return true;
    }

    public void Kill()
    {
        _events.Enqueue("kill");
        Exit(137);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _events.Enqueue("dispose");
    }
}

/// <summary>The real <see cref="ChildProcess"/>, with /bin/sh. Skipped on Windows.</summary>
public class ChildProcessTests : IDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<string, string?> NoChanges = new();

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-child-" + Guid.NewGuid().ToString("N"));

    public ChildProcessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private static ChildProcess Sh(string script, string? dir = null, IReadOnlyDictionary<string, string?>? env = null, params string[] args) =>
        ChildProcess.Start(new ChildProcessSpec("/bin/sh", ["-c", script, "sh", .. args], dir, env ?? NoChanges));

    private static async Task<List<string>> Rest(ChannelReader<string> reader)
    {
        using var cts = new CancellationTokenSource(Guard);
        var lines = new List<string>();
        await foreach (var line in reader.ReadAllAsync(cts.Token))
            lines.Add(line);
        return lines;
    }

    private static async Task<string> Next(ChannelReader<string> reader)
    {
        using var cts = new CancellationTokenSource(Guard);
        return await reader.ReadAsync(cts.Token);
    }

    [Fact]
    public async Task Runs_without_a_shell_and_reads_both_streams()
    {
        if (OperatingSystem.IsWindows())
            return;
        File.WriteAllText(Path.Combine(_dir, "marker.txt"), "here\n");
        using var child = Sh("printf '%s\\n' \"$1\"; cat marker.txt; echo oops >&2; exit 3", _dir, null, "two words; $(echo injected) 'q'");

        Assert.Equal(3, await child.Exited.WaitAsync(Guard));
        Assert.Equal(["two words; $(echo injected) 'q'", "here"], await Rest(child.StandardOutput));
        Assert.Equal(["oops"], await Rest(child.StandardError));
        Assert.True(child.Id > 0);
    }

    [Fact]
    public async Task Environment_overrides_set_and_remove_variables()
    {
        if (OperatingSystem.IsWindows())
            return;
        Environment.SetEnvironmentVariable("SPIREMIND_TEST_DROP", "present");
        Environment.SetEnvironmentVariable("SPIREMIND_TEST_INHERIT", "inherited");
        try
        {
            var env = new Dictionary<string, string?> { ["SPIREMIND_TEST_SET"] = "set", ["SPIREMIND_TEST_DROP"] = null };
            using var child = Sh("echo \"${SPIREMIND_TEST_SET-unset}\"; echo \"${SPIREMIND_TEST_DROP-unset}\"; echo \"${SPIREMIND_TEST_INHERIT-unset}\"", env: env);

            Assert.Equal(0, await child.Exited.WaitAsync(Guard));
            Assert.Equal(["set", "unset", "inherited"], await Rest(child.StandardOutput));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SPIREMIND_TEST_DROP", null);
            Environment.SetEnvironmentVariable("SPIREMIND_TEST_INHERIT", null);
        }
    }

    [Fact]
    public async Task Stdin_is_closed_so_nothing_waits_for_input()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var child = ChildProcess.Start(new ChildProcessSpec("/bin/cat", [], null, NoChanges));

        Assert.Equal(0, await child.Exited.WaitAsync(Guard));
        Assert.Empty(await Rest(child.StandardOutput));
    }

    [Fact]
    public async Task Interrupt_sends_sigint()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var child = Sh("trap 'echo got-int; exit 42' INT; echo ready; while :; do sleep 0.1; done");
        Assert.Equal("ready", await Next(child.StandardOutput));

        Assert.True(child.Interrupt());

        Assert.Equal(42, await child.Exited.WaitAsync(Guard));
        Assert.Equal(["got-int"], await Rest(child.StandardOutput));
    }

    [Fact]
    public async Task Kill_takes_the_whole_tree()
    {
        if (OperatingSystem.IsWindows())
            return;
        // The background sleep holds stdout open, so stdout only ends once it is dead too.
        using var child = Sh("sleep 30 & echo started; wait");
        Assert.Equal("started", await Next(child.StandardOutput));

        child.Kill();

        await child.Exited.WaitAsync(Guard);
        Assert.Empty(await Rest(child.StandardOutput));
    }

    [Fact]
    public async Task Dispose_kills_a_running_child()
    {
        if (OperatingSystem.IsWindows())
            return;
        var child = Sh("echo started; exec sleep 30");
        Assert.Equal("started", await Next(child.StandardOutput));
        var pid = child.Id;

        child.Dispose();

        await child.Exited.WaitAsync(Guard);
        var deadline = DateTime.UtcNow + Guard;
        while (IsAlive(pid) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        Assert.False(IsAlive(pid));
    }

    [Fact]
    public void A_missing_program_throws()
    {
        if (OperatingSystem.IsWindows())
            return;
        Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            ChildProcess.Start(new ChildProcessSpec(Path.Combine(_dir, "no-such-claude"), [], null, NoChanges)));
    }

    // Running and not a zombie, per ps.
    private static bool IsAlive(int pid)
    {
        var info = new System.Diagnostics.ProcessStartInfo("ps") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in new[] { "-o", "stat=", "-p", pid.ToString(CultureInfo.InvariantCulture) })
            info.ArgumentList.Add(argument);
        using var ps = System.Diagnostics.Process.Start(info)!;
        var stat = ps.StandardOutput.ReadToEnd().Trim();
        ps.WaitForExit();
        return stat.Length > 0 && !stat.StartsWith('Z');
    }
}
