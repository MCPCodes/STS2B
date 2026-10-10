using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class ApiBrainTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    // How the brain writes JSON for the model: '>' and non-ASCII stay as they are.
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task Tool_use_runs_the_tool_and_sends_its_result_back()
    {
        using var rig = new Rig();
        var act = new JsonObject { ["done"] = "play:0>e0", ["next"] = Snap("combat", true, version: 8) };
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "act" ? new ToolOutcome(act.DeepClone()) : Outcome(Snap("combat", true, version: 7)));
        var usage1 = Usage(input: 3000, output: 400);
        var usage2 = Usage(input: 200, output: 100, cacheRead: 3000);
        rig.Api
            .Then(Reply("tool_use", usage1, Thinking("Bash first.", "sig+/=="),
                ToolUse("toolu_1", "act", new JsonObject { ["action_id"] = "play:0>e0", ["version"] = 7 })))
            .Then(Reply("tool_use", usage2, ToolUse("toolu_2", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var actCall = Assert.Single(rig.Host.Calls, c => c.Name == "act");
        Assert.Equal("play:0>e0", actCall.Args["action_id"]!.GetValue<string>());
        Assert.Equal(7, actCall.Args["version"]!.GetValue<int>());

        var second = rig.Api[1]["messages"]!.AsArray();
        Assert.Equal(3, second.Count);
        Assert.Equal("user", second[0]!["role"]!.GetValue<string>());
        Assert.Equal("assistant", second[1]!["role"]!.GetValue<string>());
        var result = Assert.Single(second[2]!["content"]!.AsArray())!;
        Assert.Equal("user", second[2]!["role"]!.GetValue<string>());
        Assert.Equal("tool_result", result["type"]!.GetValue<string>());
        Assert.Equal("toolu_1", result["tool_use_id"]!.GetValue<string>());
        Assert.False(result["is_error"]!.GetValue<bool>());
        Assert.Equal(act.ToJsonString(Relaxed), result["content"]!.GetValue<string>());
        Assert.Contains("\"done\":\"play:0>e0\"", result["content"]!.GetValue<string>());

        // The opening message carries the state, so the first decision needs no extra call.
        var opening = Text(rig.Api[0]["messages"]![0]!);
        Assert.StartsWith(ApiBrain.Starting, opening);
        Assert.Contains("\"version\":7", opening);
        Assert.EndsWith(ApiBrain.CarryOn, opening);

        // Spend: both responses, priced from their usage.
        Assert.Equal(2, rig.Spend.Calls);
        Assert.Equal(ModelPrices.Cost("claude-opus-5-5", usage1).Usd + ModelPrices.Cost("claude-opus-5-5", usage2).Usd, rig.Spend.RunTotal);
        Assert.Contains(rig.Logged, l => l.Contains("claude-opus-5-5") && l.Contains("tool_use") && l.Contains("tools: act"));
        Assert.True(rig.Brain.IsRunning);
    }

    [Fact]
    public async Task Thinking_blocks_go_back_byte_identical()
    {
        using var rig = new Rig();
        var content = new JsonArray(
            Thinking("", "EqQBCgIYAhIM+abc/def==é"),
            new JsonObject { ["type"] = "redacted_thinking", ["data"] = "opaque+/data==" },
            new JsonObject { ["type"] = "text", ["text"] = "Bash → Strike, \"quoted\"" },
            ToolUse("toolu_9", "get_state"));
        rig.Api
            .Then(new ClaudeResponse(new JsonObject
            {
                ["id"] = "msg_1", ["type"] = "message", ["role"] = "assistant", ["model"] = "claude-opus-5-5",
                ["content"] = content.DeepClone(), ["stop_reason"] = "tool_use", ["usage"] = Usage(),
            }));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var sent = rig.Api[1]["messages"]![1]!;
        Assert.Equal("assistant", sent["role"]!.GetValue<string>());
        Assert.Equal(content.ToJsonString(), sent["content"]!.ToJsonString());
        Assert.Equal("EqQBCgIYAhIM+abc/def==é", sent["content"]![0]!["signature"]!.GetValue<string>());
    }

    [Fact]
    public async Task Wait_for_decision_waits_locally_until_there_is_a_decision()
    {
        using var rig = new Rig();
        var waits = 0;
        rig.Host.Handler = (name, _, _) => Task.FromResult(name switch
        {
            "wait_for_decision" => ++waits < 4 ? Outcome(Snap("combat", false, version: 3)) : Outcome(Snap("combat", true, version: 4)),
            _ => Outcome(Snap("combat", true, version: 2)),
        });
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_w", "wait_for_decision", new JsonObject { ["timeout_seconds"] = 30 })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var calls = rig.Host.Calls.Where(c => c.Name == "wait_for_decision").ToArray();
        Assert.Equal(4, calls.Length);
        Assert.All(calls, c =>
        {
            Assert.Equal(240, c.Args["timeout_seconds"]!.GetValue<int>());
            Assert.Equal(1, c.ApiCallsBefore);
        });
        Assert.Equal(2, rig.Api.Count);
        var result = Assert.Single(rig.Api[1]["messages"]![2]!["content"]!.AsArray())!;
        Assert.Equal("toolu_w", result["tool_use_id"]!.GetValue<string>());
        Assert.True(JsonNode.Parse(result["content"]!.GetValue<string>())!["awaiting_decision"]!.GetValue<bool>());
        Assert.Equal(3, rig.Delays.Count(d => d == TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Wait_for_decision_returns_early_for_chat()
    {
        using var rig = new Rig();
        var waits = 0;
        rig.Host.Handler = (name, _, _) => Task.FromResult(name switch
        {
            "wait_for_decision" => ++waits < 3
                ? Outcome(WithChat(Snap("combat", false), null))
                : Outcome(WithChat(Snap("combat", false), "Ironclad, take the elite")),
            _ => Outcome(Snap("combat", true)),
        });
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_w", "wait_for_decision")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        // A paused result (chat key without messages) doesn't end the wait; a message does.
        Assert.Equal(3, rig.Host.Calls.Count(c => c.Name == "wait_for_decision"));
        var result = rig.Api[1]["messages"]![2]!["content"]![0]!;
        Assert.Contains("Ironclad, take the elite", result["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task End_turn_in_a_run_is_nudged_then_waits_locally_the_second_time()
    {
        using var rig = new Rig();
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "wait_for_decision"
            ? Outcome(Snap("combat", true, version: 9))
            : Outcome(Snap("combat", true, version: 5)));
        rig.Api
            .Then(Reply("end_turn", null, TextBlock("Done for now.")))
            .Then(Reply("end_turn", null, TextBlock("Still done.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var first = rig.Api[1]["messages"]!.AsArray();
        Assert.Equal(ApiBrain.KeepPlaying, Text(first[^1]!));

        // No local wait before the first nudge; one after the second end_turn.
        var wait = Assert.Single(rig.Host.Calls, c => c.Name == "wait_for_decision");
        Assert.Equal(2, wait.ApiCallsBefore);

        var second = rig.Api[2]["messages"]!.AsArray();
        Assert.Equal(5, second.Count);
        var nudge = Text(second[^1]!);
        Assert.StartsWith(ApiBrain.KeepPlayingWithState, nudge);
        Assert.Contains("\"version\":9", nudge);
    }

    [Fact]
    public async Task End_turn_outside_a_run_polls_the_game_without_calling_the_api()
    {
        using var rig = new Rig();
        var states = 0;
        rig.Host.Handler = (name, _, _) => Task.FromResult(name switch
        {
            "get_state" => ++states switch
            {
                1 => Outcome(Snap("combat", true)),
                2 => Outcome(Snap("game_over", true)),
                3 or 4 => Outcome(Snap("menu", false)),
                _ => Outcome(Snap("map", true, version: 12)),
            },
            _ => Outcome(Snap("menu", false)),
        });
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")))
            .Then(Reply("end_turn", null, TextBlock("Run over. Good game.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var polls = rig.Host.Calls.Where(c => c.Name == "get_state").ToArray();
        Assert.Equal(5, polls.Length);
        Assert.All(polls[2..], p => Assert.Equal(2, p.ApiCallsBefore));
        Assert.DoesNotContain(rig.Host.Calls, c => c.Name == "wait_for_decision");
        Assert.Equal(2, rig.Delays.Count(d => d == TimeSpan.FromSeconds(5)));

        // game_over, then the menu, then a run screen: a new run, so a fresh conversation.
        var fresh = Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.NewRun, fresh);
        Assert.Contains("\"screen\":\"map\"", fresh);
    }

    [Fact]
    public async Task Nothing_is_sent_at_the_menu_until_chat_arrives()
    {
        using var rig = new Rig { Character = null };
        var states = 0;
        rig.Host.Handler = (_, _, _) => Task.FromResult(++states < 4
            ? Outcome(Snap("menu", false))
            : Outcome(WithChat(Snap("menu", false), "Silent, go poison this run")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(4, rig.Host.Calls.Length);
        Assert.All(rig.Host.Calls, c => Assert.Equal(0, c.ApiCallsBefore));
        var opening = Text(rig.Api[0]["messages"]![0]!);
        Assert.Contains("## New chat messages for you", opening);
        Assert.Contains("Silent, go poison this run", opening);
        Assert.Contains("\"screen\":\"menu\"", opening);
    }

    [Theory]
    [InlineData("map", 62_000, true)]
    [InlineData("map", 59_000, false)]
    [InlineData("combat", 62_000, false)]
    [InlineData("combat", 151_000, true)]
    public async Task Fresh_context_thresholds(string screen, int promptTokens, bool refreshed)
    {
        using var rig = new Rig();
        rig.Host.Handler = (_, _, _) => Task.FromResult(Outcome(Snap(screen, true)));
        rig.Api
            .Then(Reply("tool_use", Usage(input: 5000), ToolUse("toolu_1", "get_state")))
            .Then(Reply("tool_use", Usage(input: 2000, cacheRead: promptTokens - 2000), ToolUse("toolu_2", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var messages = rig.Api[2]["messages"]!.AsArray();
        Assert.Equal(refreshed ? 1 : 5, messages.Count);
        Assert.Equal(promptTokens, rig.Brain.LastPromptTokens);
    }

    [Fact]
    public async Task Fresh_context_keeps_system_and_tools_and_carries_journal_chat_and_state()
    {
        using var rig = new Rig();
        rig.WriteFile("runs/current.md", "# Ironclad, started 2026-10-10, Neow: gold\n\n## Build plan\nStrength via Inflame.\n");
        rig.Chat = Enumerable.Range(1, 12).Select(i => $"Mark: chat-{i:00}").ToList();
        var states = 0;
        rig.Host.Handler = (name, _, _) => Task.FromResult(name switch
        {
            "say" => Outcome(WithChat(new JsonObject { ["said"] = "Aye." }, "Ironclad, skip the shop")),
            _ => ++states < 3 ? Outcome(Snap("combat", true)) : Outcome(Snap("map", true, version: 30, marker: "node-marker-123")),
        });
        rig.Api
            .Then(Reply("tool_use", Usage(input: 5000), ToolUse("toolu_1", "get_state")))
            .Then(Reply("tool_use", Usage(input: 2000, cacheRead: 60_000), ToolUse("toolu_2", "get_state"), ToolUse("toolu_3", "say", new JsonObject { ["text"] = "Aye." })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var fresh = rig.Api[2];
        var message = Assert.Single(fresh["messages"]!.AsArray())!;
        Assert.Equal("user", message["role"]!.GetValue<string>());
        var text = Text(message);
        Assert.StartsWith(ApiBrain.Refreshed, text);
        Assert.Contains("Strength via Inflame.", text);
        Assert.Contains("chat-12", text);
        Assert.Contains("chat-03", text);
        Assert.DoesNotContain("chat-02", text);
        Assert.Contains("node-marker-123", text);
        Assert.Contains("Ironclad, skip the shop", text);
        Assert.EndsWith(ApiBrain.CarryOn, text);

        foreach (var key in new[] { "system", "tools" })
        {
            Assert.Equal(rig.Api[0][key]!.ToJsonString(), fresh[key]!.ToJsonString());
            Assert.Equal(rig.Api[1][key]!.ToJsonString(), fresh[key]!.ToJsonString());
        }
    }

    [Fact]
    public async Task A_new_character_rebuilds_the_prompt_and_starts_over()
    {
        using var rig = new Rig { Character = null };
        rig.WriteFile("strategy/characters/silent.md", "# Silent\nSILENT-NOTES");
        rig.Host.Handler = (name, _, _) =>
        {
            if (name == "act") rig.Character = "The Silent";
            return Task.FromResult(Outcome(Snap("map", true)));
        };
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_1", "act", new JsonObject { ["action_id"] = "map:0" })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.DoesNotContain("SILENT-NOTES", SystemText(rig.Api[0]));
        Assert.Contains("SILENT-NOTES", SystemText(rig.Api[1]));
        var message = Assert.Single(rig.Api[1]["messages"]!.AsArray())!;
        Assert.StartsWith(ApiBrain.NewRun, Text(message));
    }

    [Fact]
    public async Task A_model_switch_starts_a_fresh_conversation()
    {
        using var rig = new Rig();
        rig.Host.Handler = (name, _, _) =>
        {
            if (name == "say") rig.Settings = rig.Settings with { Model = "sonnet", Effort = "low" };
            return Task.FromResult(Outcome(Snap("combat", true, marker: "after-switch")));
        };
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_1", "say", new JsonObject { ["text"] = "Switching." })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal("claude-sonnet-5-5", rig.Api[1]["model"]!.GetValue<string>());
        Assert.Equal("low", rig.Api[1]["output_config"]!["effort"]!.GetValue<string>());
        var text = Text(Assert.Single(rig.Api[1]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.ModelChanged, text);
        Assert.Contains("after-switch", text);
        Assert.Equal(rig.Api[0]["system"]!.ToJsonString(), rig.Api[1]["system"]!.ToJsonString());
    }

    [Fact]
    public async Task An_effort_switch_alone_starts_a_fresh_conversation()
    {
        using var rig = new Rig();
        rig.Host.Handler = (name, _, _) =>
        {
            if (name == "say") rig.Settings = rig.Settings with { Effort = "low" };
            return Task.FromResult(Outcome(Snap("combat", true, marker: "after-effort")));
        };
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_1", "say", new JsonObject { ["text"] = "Quicker now." })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(rig.Api[0]["model"]!.GetValue<string>(), rig.Api[1]["model"]!.GetValue<string>());
        Assert.Equal("low", rig.Api[1]["output_config"]!["effort"]!.GetValue<string>());
        var text = Text(Assert.Single(rig.Api[1]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.ModelChanged, text);
        Assert.Contains("after-effort", text);
    }

    [Fact]
    public async Task Monthly_cap_stops_before_any_request()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { MonthlyBudgetUsd = 10m };
        rig.Spend.Add(10m, rig.Now);

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.MonthlyCap, reason);
        Assert.Equal(0, rig.Api.Count);
        Assert.Contains("$10.00 of $10.00", message);
        Assert.Equal([message], rig.Announced);
        Assert.False(rig.Brain.IsRunning);
    }

    [Theory]
    [InlineData(ClaudeErrorKind.OutOfCredit, 400, BrainStopReason.OutOfCredit)]
    [InlineData(ClaudeErrorKind.SpendLimit, 429, BrainStopReason.SpendLimit)]
    [InlineData(ClaudeErrorKind.Unauthorized, 401, BrainStopReason.Unauthorized)]
    public async Task Errors_that_need_the_human_stop_with_their_reason(ClaudeErrorKind kind, int status, BrainStopReason expected)
    {
        using var rig = new Rig();
        rig.Api.Then(new ClaudeApiException(kind, status, "some_error", "Your credit balance is too low", "req_1"));

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(expected, reason);
        Assert.Equal(new ClaudeApiException(kind, status, null, "", null).Summary, message);
        Assert.Equal([message], rig.Announced);
        Assert.Equal(1, rig.Api.Count);
        Assert.DoesNotContain(rig.Delays, d => d >= TimeSpan.FromSeconds(30));
        Assert.False(rig.Brain.IsRunning);
    }

    [Fact]
    public async Task Transient_errors_retry_the_same_request_with_growing_pauses()
    {
        using var rig = new Rig();
        foreach (var kind in new[] { ClaudeErrorKind.Overloaded, ClaudeErrorKind.Network, ClaudeErrorKind.Server, ClaudeErrorKind.RateLimited, ClaudeErrorKind.Overloaded, ClaudeErrorKind.Network })
            rig.Api.Then(new ClaudeApiException(kind, null, null, "nope", null, attempts: 5));
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(8, rig.Api.Count);
        var first = rig.Api[0].ToJsonString();
        Assert.All(Enumerable.Range(0, 7), i => Assert.Equal(first, rig.Api[i].ToJsonString()));
        Assert.Equal([30, 60, 120, 240, 300, 300], rig.Delays.Where(d => d >= TimeSpan.FromSeconds(30)).Select(d => (int)d.TotalSeconds));
        Assert.Equal([ApiBrain.Retrying], rig.Announced);
        Assert.True(rig.Brain.IsRunning);
        Assert.False(rig.Stopped.Task.IsCompleted);
    }

    [Fact]
    public async Task A_long_retry_after_is_honoured()
    {
        using var rig = new Rig();
        rig.Api.Then(new ClaudeApiException(ClaudeErrorKind.RateLimited, 429, "rate_limit_error", "slow down", null, retryAfter: TimeSpan.FromMinutes(10)));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(TimeSpan.FromMinutes(10), Assert.Single(rig.Delays, d => d >= TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task Invalid_request_stops_with_error()
    {
        using var rig = new Rig();
        rig.Api.Then(new ClaudeApiException(ClaudeErrorKind.InvalidRequest, 400, "invalid_request_error", "tools.0: bad schema", "req_2"));

        rig.Brain.Start();
        var (reason, _) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Single(rig.Announced);
        Assert.Contains(rig.Logged, l => l.Contains("tools.0: bad schema"));
    }

    [Fact]
    public async Task Prompt_too_long_starts_a_fresh_conversation()
    {
        using var rig = new Rig();
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")))
            .Then(new ClaudeApiException(ClaudeErrorKind.InvalidRequest, 400, "invalid_request_error", "prompt is too long: 1000001 tokens > 1000000 maximum", null));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(3, rig.Api[1]["messages"]!.AsArray().Count);
        Assert.StartsWith(ApiBrain.Refreshed, Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!));
    }

    [Fact]
    public async Task Context_window_exceeded_starts_a_fresh_conversation()
    {
        using var rig = new Rig();
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")))
            .Then(Reply("model_context_window_exceeded", null));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.StartsWith(ApiBrain.Refreshed, Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!));
    }

    [Fact]
    public async Task Max_tokens_answers_cut_off_calls_without_running_them()
    {
        using var rig = new Rig();
        rig.Api.Then(Reply("max_tokens", null, TextBlock("Let me think about"), ToolUse("toolu_cut", "act", new JsonObject { ["action_id"] = "end_turn" })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.DoesNotContain(rig.Host.Calls, c => c.Name == "act");
        var blocks = rig.Api[1]["messages"]!.AsArray()[^1]!["content"]!.AsArray();
        Assert.Equal(2, blocks.Count);
        Assert.Equal("toolu_cut", blocks[0]!["tool_use_id"]!.GetValue<string>());
        Assert.True(blocks[0]!["is_error"]!.GetValue<bool>());
        Assert.Equal(ApiBrain.CutOff, blocks[1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Refusal_announces_and_stops_with_error()
    {
        using var rig = new Rig();
        rig.Api.Then(Reply("refusal", null));

        rig.Brain.Start();
        var (reason, _) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Contains(rig.Announced, a => a.Contains("refusal"));
    }

    [Fact]
    public async Task A_model_that_keeps_ending_its_turn_is_stopped()
    {
        using var rig = new Rig();
        for (var i = 0; i < 10; i++)
            rig.Api.Then(Reply("end_turn", null, TextBlock("No.")));

        rig.Brain.Start();
        var (reason, _) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(6, rig.Api.Count);
    }

    [Fact]
    public async Task Stop_cancels_a_request_in_flight()
    {
        using var rig = new Rig();
        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        await rig.Brain.StopAsync().WaitAsync(Guard);

        var (reason, _) = await rig.Stopped.Task.WaitAsync(Guard);
        Assert.Equal(BrainStopReason.UserStop, reason);
        Assert.False(rig.Brain.IsRunning);
        Assert.False(rig.StoppedWhileRunning);
        Assert.Empty(rig.Announced);
    }

    [Fact]
    public async Task Stop_cancels_a_local_wait()
    {
        using var rig = new Rig();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Host.Handler = async (name, _, ct) =>
        {
            if (name != "wait_for_decision")
                return Outcome(Snap("combat", true));
            waiting.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        };
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_w", "wait_for_decision")));

        rig.Brain.Start();
        await waiting.Task.WaitAsync(Guard);
        await rig.Brain.StopAsync().WaitAsync(Guard);

        Assert.Equal(BrainStopReason.UserStop, (await rig.Stopped.Task.WaitAsync(Guard)).Reason);
        Assert.Equal(1, rig.Api.Count);
    }

    [Fact]
    public async Task Request_shape()
    {
        using var rig = new Rig();
        rig.Host.Specs =
        [
            .. Rig.DefaultSpecs,
            new ToolSpec("nested", "Nested schemas", new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["options"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["steps"] = new JsonObject
                            {
                                ["type"] = "array",
                                ["minItems"] = 2,
                                ["items"] = new JsonObject
                                {
                                    ["type"] = "object",
                                    ["properties"] = new JsonObject { ["id"] = new JsonObject { ["type"] = "string", ["maxLength"] = 9 } },
                                },
                            },
                            ["either"] = new JsonObject
                            {
                                ["anyOf"] = new JsonArray(new JsonObject { ["type"] = "object" }, new JsonObject { ["type"] = "string" }),
                            },
                        },
                    },
                },
            }),
        ];
        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var request = rig.Api[0];
        Assert.Equal("claude-opus-5-5", request["model"]!.GetValue<string>());
        Assert.Equal(16000, request["max_tokens"]!.GetValue<int>());
        foreach (var absent in new[] { "temperature", "top_p", "top_k", "thinking", "tool_choice" })
            Assert.False(request.ContainsKey(absent), absent);
        Assert.Equal("high", request["output_config"]!["effort"]!.GetValue<string>());

        var system = Assert.Single(request["system"]!.AsArray())!;
        Assert.Equal("text", system["type"]!.GetValue<string>());
        Assert.Equal("""{"type":"ephemeral","ttl":"1h"}""", system["cache_control"]!.ToJsonString());
        Assert.Equal("""{"type":"ephemeral"}""", request["cache_control"]!.ToJsonString());

        var tools = request["tools"]!.AsArray().Select(t => t!.AsObject()).ToArray();
        Assert.Equal(["get_state", "wait_for_decision", "act", "nested", "notes_read", "notes_write", "notes_append"], tools.Select(t => t["name"]!.GetValue<string>()));
        Assert.All(tools, t =>
        {
            Assert.True(t["strict"]!.GetValue<bool>());
            Assert.Equal(["name", "description", "input_schema", "strict"], t.Select(p => p.Key));
            AssertStrict(t["input_schema"]!.AsObject());
        });

        // Constraints strict schemas don't take move into the description.
        var timeout = tools[1]["input_schema"]!["properties"]!["timeout_seconds"]!.AsObject();
        Assert.False(timeout.ContainsKey("minimum"));
        Assert.False(timeout.ContainsKey("maximum"));
        Assert.False(timeout.ContainsKey("default"));
        Assert.Equal("Seconds (minimum 1, maximum 240, default 60)", timeout["description"]!.GetValue<string>());
        var steps = tools[3]["input_schema"]!["properties"]!["options"]!["properties"]!["steps"]!.AsObject();
        Assert.Equal("(minItems 2)", steps["description"]!.GetValue<string>());

        // The host's own specs are untouched.
        Assert.Equal(1, rig.Host.Specs[1].InputSchema["properties"]!["timeout_seconds"]!["minimum"]!.GetValue<int>());
        Assert.False(rig.Host.Specs[0].InputSchema.ContainsKey("additionalProperties"));
    }

    [Fact]
    public async Task Transcript_has_one_line_per_exchange_without_system_text()
    {
        using var rig = new Rig { UseTranscript = true };
        rig.Api
            .Then(Reply("tool_use", Usage(input: 1000, output: 10), ToolUse("toolu_1", "get_state")))
            .Then(Reply("tool_use", Usage(input: 50, output: 10, cacheRead: 1000), ToolUse("toolu_2", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var lines = File.ReadAllLines(Path.Combine(rig.Dir, "transcripts", "2026-10-10.jsonl")).Select(l => JsonNode.Parse(l)!).ToArray();
        Assert.Equal(2, lines.Length);
        Assert.Equal(0, lines[0]["turn"]!.GetValue<int>());
        Assert.Single(lines[0]["new_messages"]!.AsArray());
        Assert.NotEmpty(lines[0]["system_files"]!.AsArray());
        Assert.Equal(2, lines[1]["new_messages"]!.AsArray().Count);
        Assert.Equal("tool_use", lines[1]["stop_reason"]!.GetValue<string>());
        Assert.Equal(1000, lines[1]["usage"]!["cache_read_input_tokens"]!.GetValue<int>());
        Assert.Equal(ModelPrices.Cost("claude-opus-5-5", Usage(input: 50, output: 10, cacheRead: 1000)).Usd, lines[1]["cost_usd"]!.GetValue<decimal>());

        var everything = string.Join("\n", File.ReadAllLines(Path.Combine(rig.Dir, "transcripts", "2026-10-10.jsonl")).Concat(rig.Logged));
        Assert.DoesNotContain(Rig.PromptMarker, everything);
        Assert.DoesNotContain(Rig.FakeKey, everything);
    }

    // ---- Review fixes ----

    [Fact]
    public async Task Max_tokens_in_a_row_stop_the_brain()
    {
        using var rig = new Rig();
        for (var i = 0; i < 6; i++)
            rig.Api.Then(Reply("max_tokens", Usage(output: 16000), Thinking("Hmm...", "sig")));

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(3, rig.Api.Count);
        Assert.Contains("output limit 3 times in a row", message);
        Assert.Equal([message], rig.Announced);
    }

    [Fact]
    public async Task Any_other_reply_clears_the_cut_off_count()
    {
        using var rig = new Rig();
        rig.Api
            .Then(Reply("max_tokens", null, Thinking("a", "s")))
            .Then(Reply("max_tokens", null, Thinking("b", "s")))
            .Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")))
            .Then(Reply("max_tokens", null, Thinking("c", "s")))
            .Then(Reply("max_tokens", null, Thinking("d", "s")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(6, rig.Api.Count);
        Assert.True(rig.Brain.IsRunning);
    }

    [Fact]
    public async Task A_fresh_conversation_does_not_clear_the_cut_off_count()
    {
        using var rig = new Rig();
        rig.Host.Handler = (_, _, _) => Task.FromResult(Outcome(Snap("map", true)));
        rig.Api
            .Then(Reply("max_tokens", Usage(input: 5000), Thinking("a", "s")))
            .Then(Reply("max_tokens", Usage(input: 2000, cacheRead: 70_000), Thinking("b", "s")))
            .Then(Reply("max_tokens", Usage(input: 2000), Thinking("c", "s")));

        rig.Brain.Start();
        var (reason, _) = await rig.Stopped.Task.WaitAsync(Guard);

        // The second cut-off pushed the conversation past the map threshold and it restarted;
        // the third still stops the brain.
        Assert.Equal(BrainStopReason.Error, reason);
        Assert.StartsWith(ApiBrain.Refreshed, Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!));
        Assert.Equal(3, rig.Api.Count);
    }

    [Theory]
    [InlineData(85_000, true)]
    [InlineData(75_000, false)]
    public async Task Haiku_refreshes_before_its_long_prompt_tier(int promptTokens, bool refreshed)
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Model = "haiku" };
        rig.Host.Handler = (_, _, _) => Task.FromResult(Outcome(Snap("combat", true)));
        rig.Api
            .Then(Reply("tool_use", Usage(input: 5000), ToolUse("toolu_1", "get_state")))
            .Then(Reply("tool_use", Usage(input: 2000, cacheRead: promptTokens - 2000), ToolUse("toolu_2", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(refreshed ? 1 : 5, rig.Api[2]["messages"]!.AsArray().Count);
    }

    [Fact]
    public async Task Json_for_the_model_keeps_symbols_and_non_ascii_as_they_are()
    {
        using var rig = new Rig();
        JsonObject Fancy(long version)
        {
            var snap = Snap("combat", true, version: version, marker: "Strike+ on the enemy's Jaw Worm \u2014 \"6\" d\u00e9g\u00e2ts");
            snap["actions"] = new JsonArray(new JsonObject { ["id"] = "play:0>e0", ["label"] = "Play Strike+ on Jaw Worm" });
            return snap;
        }
        rig.Host.Handler = (name, _, _) => Task.FromResult(Outcome(Fancy(name == "wait_for_decision" ? 3 : 2)));
        rig.Api
            .Then(Reply("tool_use", Usage(input: 5000), ToolUse("toolu_1", "get_state")))
            .Then(Reply("end_turn", null, TextBlock("Done.")))
            .Then(Reply("end_turn", null, TextBlock("Done.")))
            .Then(Reply("tool_use", Usage(input: 2000, cacheRead: 200_000), ToolUse("toolu_2", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var opening = Text(rig.Api[0]["messages"]![0]!);
        var toolResult = rig.Api[1]["messages"]![2]!["content"]![0]!["content"]!.GetValue<string>();
        var nudge = Text(rig.Api[3]["messages"]!.AsArray()[^1]!);
        var fresh = Text(Assert.Single(rig.Api[4]["messages"]!.AsArray())!);
        foreach (var text in new[] { opening, toolResult, nudge, fresh })
        {
            Assert.Contains("\"id\":\"play:0>e0\"", text);
            Assert.Contains("Strike+ on the enemy's Jaw Worm \u2014 \\\"6\\\" d\u00e9g\u00e2ts", text);
            Assert.DoesNotContain("\\u", text);
        }
    }

    [Fact]
    public async Task An_announce_that_throws_still_ends_the_loop_cleanly()
    {
        using var rig = new Rig { AnnounceThrows = true, LogThrows = true };
        rig.Api.Then(Reply("refusal", null));

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Contains("refusal", message);
        Assert.False(rig.Brain.IsRunning);
        await rig.Brain.StopAsync().WaitAsync(Guard);
        Assert.True(rig.Brain.Start());
    }

    [Fact]
    public async Task End_turn_while_the_game_is_busy_waits_locally_before_any_nudge()
    {
        using var rig = new Rig();
        var waits = 0;
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "wait_for_decision"
            ? Outcome(Snap("combat", ++waits >= 3, version: 4))
            : Outcome(Snap("combat", false, version: 3)));
        rig.Api.Then(Reply("end_turn", null, TextBlock("Paused, so I'll stop here.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var calls = rig.Host.Calls.Where(c => c.Name == "wait_for_decision").ToArray();
        Assert.Equal(3, calls.Length);
        Assert.All(calls, c => Assert.Equal(1, c.ApiCallsBefore));
        Assert.Equal(2, rig.Api.Count);
        Assert.StartsWith(ApiBrain.KeepPlayingWithState, Text(rig.Api[1]["messages"]!.AsArray()[^1]!));
    }

    [Fact]
    public async Task Reading_tools_between_ended_turns_do_not_reset_the_guard()
    {
        using var rig = new Rig();
        for (var i = 0; i < 20; i++)
            rig.Api
                .Then(Reply("tool_use", null, ToolUse($"toolu_{i}", "get_state")))
                .Then(Reply("end_turn", null, TextBlock("Nothing to do.")));

        rig.Brain.Start();
        var (reason, _) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(12, rig.Api.Count);
    }

    [Fact]
    public async Task A_chat_message_clears_the_end_turn_count_during_a_run()
    {
        using var rig = new Rig();
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "wait_for_decision"
            ? Outcome(WithChat(Snap("combat", false), "Ironclad, what's the plan?"))
            : Outcome(Snap("combat", false)));
        for (var i = 0; i < 10; i++)
            rig.Api.Then(Reply("end_turn", null, TextBlock("No reply needed.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(11, rig.Api.Count);
        Assert.True(rig.Brain.IsRunning);
    }

    [Fact]
    public async Task A_wait_replaced_by_another_client_stops_the_brain()
    {
        using var rig = new Rig();
        rig.Host.Handler = (name, _, _) => name == "wait_for_decision"
            ? Task.FromException<ToolOutcome>(new OperationCanceledException())
            : Task.FromResult(Outcome(Snap("combat", true)));
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_w", "wait_for_decision")));
        for (var i = 0; i < 5; i++)
            rig.Api.Then(Reply("tool_use", null, ToolUse($"toolu_{i}", "wait_for_decision")));

        rig.Brain.Start();
        var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Equal(ApiBrain.AnotherClient, message);
        Assert.Equal(1, rig.Api.Count);
        Assert.Equal([ApiBrain.AnotherClient], rig.Announced);
    }

    [Fact]
    public async Task A_modal_or_error_at_the_menu_is_not_a_run()
    {
        using var rig = new Rig { Character = null };
        var states = 0;
        rig.Host.Handler = (_, _, _) => Task.FromResult(++states switch
        {
            1 => Outcome(Snap("menu", false)),
            2 => Outcome(Snap("modal", false)),
            3 => Outcome(Snap("error", false)),
            4 => Outcome(Snap("unknown", false)),
            _ => Outcome(WithChat(Snap("modal", false), "Silent, ready?")),
        });

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(5, rig.Host.Calls.Length);
        Assert.All(rig.Host.Calls, c => Assert.Equal(0, c.ApiCallsBefore));
        Assert.Contains("Silent, ready?", Text(rig.Api[0]["messages"]![0]!));
    }

    [Fact]
    public async Task A_modal_during_a_run_keeps_the_run()
    {
        using var rig = new Rig();
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "get_state" && rig.Api.Count > 0
            ? Outcome(Snap("modal", true))
            : Outcome(Snap("combat", true)));
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")))
            .Then(Reply("end_turn", null, TextBlock("Done.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        // Still in the run: the first ended turn gets the nudge, not an idle poll.
        Assert.Equal(ApiBrain.KeepPlaying, Text(rig.Api[2]["messages"]!.AsArray()[^1]!));
    }

    [Fact]
    public async Task Ending_turns_at_the_menu_never_stops_the_brain()
    {
        using var rig = new Rig { Character = null };
        rig.Host.Handler = (_, _, _) => Task.FromResult(Outcome(WithChat(Snap("menu", false), "ready?")));
        for (var i = 0; i < 10; i++)
            rig.Api.Then(Reply("end_turn", null, TextBlock("Not for me.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(11, rig.Api.Count);
        Assert.True(rig.Brain.IsRunning);
        Assert.StartsWith(ApiBrain.KeepPlayingWithState, Text(rig.Api[10]["messages"]!.AsArray()[^1]!));
    }

    [Fact]
    public async Task A_new_run_with_the_same_character_reloads_the_notes()
    {
        using var rig = new Rig();
        rig.WriteFile("strategy/learned.md", "OLD-LESSON");
        var states = 0;
        rig.Host.Handler = (name, _, _) => Task.FromResult(++states switch
        {
            1 => Outcome(Snap("combat", true)),
            2 => Outcome(Snap("game_over", true)),
            3 => Outcome(Snap("menu", false)),
            _ => Outcome(Snap("event", true, marker: "neow")),
        });
        rig.Api
            .Then(Reply("tool_use", null,
                ToolUse("toolu_1", "notes_write", new JsonObject { ["file"] = "strategy/learned.md", ["content"] = "NEW-LESSON" }),
                ToolUse("toolu_2", "get_state")))
            .Then(Reply("end_turn", null, TextBlock("Good game.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Contains("OLD-LESSON", SystemText(rig.Api[0]));
        var system = SystemText(rig.Api[2]);
        Assert.Contains("NEW-LESSON", system);
        Assert.DoesNotContain("OLD-LESSON", system);
        var fresh = Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.NewRun, fresh);
        Assert.Contains("neow", fresh);
    }

    [Fact]
    public async Task Plays_through_the_real_bot_tools()
    {
        var game = new FakeGame { ActionDelay = TimeSpan.Zero };
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner) { PollInterval = TimeSpan.FromMilliseconds(1), SettleTime = TimeSpan.Zero };
        var tools = new BotTools(game, waiter, versioner, new ChatHub()) { ActSettleTimeout = TimeSpan.FromSeconds(5) };
        using var rig = new Rig { Tools = new BotToolsHost(tools) };
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_1", "act", new JsonObject { ["action_id"] = "play:0>e0" })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var names = rig.Api[0]["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>());
        Assert.Equal(tools.Specs.Select(s => s.Name).Concat(["notes_read", "notes_write", "notes_append"]), names);
        Assert.Contains("\"id\":\"play:0>e0\"", Text(rig.Api[0]["messages"]![0]!));

        var result = rig.Api[1]["messages"]![2]!["content"]![0]!;
        Assert.False(result["is_error"]!.GetValue<bool>());
        var payload = JsonNode.Parse(result["content"]!.GetValue<string>())!;
        Assert.Equal("play:0>e0", payload["done"]!.GetValue<string>());
        Assert.Equal(34, payload["next"]!["state"]!["enemies"]![0]!["hp"]!.GetValue<int>());
    }

    // ---- Helpers ----

    private static void AssertStrict(JsonObject schema)
    {
        if (schema["type"]?.GetValue<string>() == "object")
        {
            Assert.False(schema["additionalProperties"]!.GetValue<bool>());
            Assert.NotNull(schema["properties"]);
        }
        foreach (var key in new[] { "minimum", "maximum", "default", "maxLength", "minLength" })
            Assert.False(schema.ContainsKey(key), key);
        if (schema["properties"] is JsonObject properties)
            foreach (var (_, p) in properties) AssertStrict(p!.AsObject());
        if (schema["items"] is JsonObject items) AssertStrict(items);
        if (schema["anyOf"] is JsonArray anyOf)
            foreach (var s in anyOf) AssertStrict(s!.AsObject());
    }

    internal static JsonObject Snap(string screen, bool awaiting, long version = 1, string? marker = null) => new()
    {
        ["version"] = version,
        ["screen"] = screen,
        ["awaiting_decision"] = awaiting,
        ["state"] = marker is null ? new JsonObject() : new JsonObject { ["marker"] = marker },
        ["actions"] = new JsonArray(),
    };

    /// <summary>A tool result with the chat key; null text gives a paused key without messages.</summary>
    private static JsonObject WithChat(JsonObject payload, string? text)
    {
        var chat = new JsonObject { ["you_are"] = "Ironclad" };
        if (text is null) chat["paused"] = true;
        else chat["messages"] = new JsonArray(new JsonObject { ["from"] = "Mark", ["text"] = text, ["seconds_ago"] = 0 });
        payload["chat"] = chat;
        return payload;
    }

    internal static ToolOutcome Outcome(JsonNode payload, bool isError = false) => new(payload, isError);

    private static JsonObject Usage(int input = 100, int output = 50, int cacheRead = 0, int cacheWrite = 0) => new()
    {
        ["input_tokens"] = input,
        ["output_tokens"] = output,
        ["cache_read_input_tokens"] = cacheRead,
        ["cache_creation_input_tokens"] = cacheWrite,
    };

    private static ClaudeResponse Reply(string stopReason, JsonObject? usage, params JsonObject[] content) => new(new JsonObject
    {
        ["id"] = "msg_" + Guid.NewGuid().ToString("N"),
        ["type"] = "message",
        ["role"] = "assistant",
        ["model"] = "claude-opus-5-5",
        ["content"] = new JsonArray(content),
        ["stop_reason"] = stopReason,
        ["usage"] = usage ?? Usage(),
    });

    private static JsonObject ToolUse(string id, string name, JsonObject? input = null) => new()
    {
        ["type"] = "tool_use",
        ["id"] = id,
        ["name"] = name,
        ["input"] = input ?? new JsonObject(),
    };

    private static JsonObject Thinking(string text, string signature) => new()
    {
        ["type"] = "thinking",
        ["thinking"] = text,
        ["signature"] = signature,
    };

    private static JsonObject TextBlock(string text) => new() { ["type"] = "text", ["text"] = text };

    /// <summary>The text of a user message whose content is a single text block.</summary>
    private static string Text(JsonNode message) => Assert.Single(message["content"]!.AsArray())!["text"]!.GetValue<string>();

    private static string SystemText(JsonObject request) => request["system"]![0]!["text"]!.GetValue<string>();
}

public class PromptBuilderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-prompt-" + Guid.NewGuid().ToString("N"));

    public PromptBuilderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void Builds_play_prompt_notes_and_appendix_in_order()
    {
        Write(".claude/commands/play.md", "---\ndescription: Play\nargument-hint: \"[how long]\"\n---\n\nScope for this session: $ARGUMENTS (default).\n");
        Write("strategy/general.md", "# General\nGENERAL-NOTES\n");
        Write("strategy/characters/ironclad.md", "# Ironclad\nIRONCLAD-NOTES\n");
        Write("strategy/characters/silent.md", "SILENT-NOTES");
        Write("strategy/learned.md", "LEARNED-NOTES");
        Write("strategy/coop.md", "COOP-NOTES");

        var prompt = PromptBuilder.Build(_dir, "The Ironclad", multiplayer: false);

        Assert.StartsWith("Scope for this session: the whole run (default).", prompt.Text);
        Assert.DoesNotContain("argument-hint", prompt.Text);
        Assert.DoesNotContain("$ARGUMENTS", prompt.Text);
        Assert.Contains("<file path=\"strategy/characters/ironclad.md\">\n# Ironclad\nIRONCLAD-NOTES\n</file>", prompt.Text);
        Assert.DoesNotContain("SILENT-NOTES", prompt.Text);
        Assert.DoesNotContain("COOP-NOTES", prompt.Text);

        var order = new[] { "Scope for this session", "## Strategy notes", "GENERAL-NOTES", "IRONCLAD-NOTES", "LEARNED-NOTES", "## Playing from inside the game" }
            .Select(s => prompt.Text.IndexOf(s, StringComparison.Ordinal)).ToArray();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Contains("notes_read", prompt.Text);
        Assert.Contains("act_sequence", prompt.Text);

        Assert.Equal(
            [".claude/commands/play.md", "strategy/general.md", "strategy/characters/ironclad.md", "strategy/learned.md"],
            prompt.Files.Select(f => Path.GetRelativePath(_dir, f).Replace('\\', '/')));
    }

    [Fact]
    public void Boss_and_elite_notes_follow_the_general_notes()
    {
        Write("strategy/general.md", "GENERAL-NOTES (see bosses-and-elites.md)");
        Write("strategy/bosses-and-elites.md", "BOSS-NOTES");
        Write("strategy/learned.md", "LEARNED-NOTES");

        var prompt = PromptBuilder.Build(_dir, null, multiplayer: false);

        Assert.Contains("<file path=\"strategy/bosses-and-elites.md\">\nBOSS-NOTES\n</file>", prompt.Text);
        var order = new[] { "GENERAL-NOTES", "BOSS-NOTES", "LEARNED-NOTES" }.Select(t => prompt.Text.IndexOf(t, StringComparison.Ordinal)).ToArray();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Equal(["general.md", "bosses-and-elites.md", "learned.md"], prompt.Files.Select(Path.GetFileName));
        Assert.Contains("notes_append", prompt.Text);
    }

    [Fact]
    public void Coop_notes_only_in_multiplayer_and_missing_files_are_skipped()
    {
        Write("strategy/coop.md", "COOP-NOTES");

        var solo = PromptBuilder.Build(_dir, null, multiplayer: false);
        Assert.Empty(solo.Files);
        Assert.DoesNotContain("## Strategy notes", solo.Text);
        Assert.StartsWith("## Playing from inside the game", solo.Text);

        var coop = PromptBuilder.Build(_dir, "Necrobinder", multiplayer: true);
        Assert.Contains("COOP-NOTES", coop.Text);
        Assert.Equal("coop.md", Path.GetFileName(Assert.Single(coop.Files)));
    }

    [Fact]
    public void Learned_notes_come_from_the_notes_folder_when_they_exist_there()
    {
        Write("strategy/learned.md", "OLD-LESSONS");
        var notes = Path.Combine(_dir, "notes");
        Directory.CreateDirectory(Path.Combine(notes, "strategy"));

        Assert.Contains("OLD-LESSONS", PromptBuilder.Build(_dir, null, false, notes).Text);

        File.WriteAllText(Path.Combine(notes, "strategy", "learned.md"), "NEW-LESSONS");
        var prompt = PromptBuilder.Build(_dir, null, false, notes);
        Assert.Contains("NEW-LESSONS", prompt.Text);
        Assert.DoesNotContain("OLD-LESSONS", prompt.Text);
    }

    [Theory]
    [InlineData("Ironclad", "strategy/characters/ironclad.md")]
    [InlineData("  the Silent ", "strategy/characters/silent.md")]
    [InlineData("../../etc/passwd", null)]
    [InlineData("Defect/../x", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Character_file(string? character, string? expected) =>
        Assert.Equal(expected, PromptBuilder.CharacterFile(character));

    [Theory]
    [InlineData("---\na: b\n---\nBody", "Body")]
    [InlineData("---\r\na: b\r\n---\r\nBody\r\n", "Body\r\n")]
    [InlineData("﻿---\n---\nBody", "Body")]
    [InlineData("No frontmatter\n---\nstill body", "No frontmatter\n---\nstill body")]
    [InlineData("Text\n---\na: b\n---\n", "Text\n---\na: b\n---\n")]
    public void Strip_frontmatter(string text, string expected) =>
        Assert.Equal(expected, PromptBuilder.StripFrontmatter(text));
}

public class NotesToolsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-notes-" + Guid.NewGuid().ToString("N"));
    private readonly NotesTools _notes;

    public NotesToolsTests()
    {
        Directory.CreateDirectory(_dir);
        _notes = new NotesTools(_dir, () => new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private ToolOutcome Call(string name, JsonObject args) => _notes.CallAsync(name, args, CancellationToken.None).GetAwaiter().GetResult();

    private ToolOutcome Write(string file, string content) =>
        Call("notes_write", new JsonObject { ["file"] = file, ["content"] = content });

    [Fact]
    public void Reading_a_missing_file_is_empty()
    {
        var outcome = Call("notes_read", new JsonObject { ["file"] = "runs/current.md" });
        Assert.False(outcome.IsError);
        Assert.False(outcome.Payload["exists"]!.GetValue<bool>());
        Assert.Equal("", outcome.Payload["content"]!.GetValue<string>());
        Assert.Equal("", _notes.ReadText(NotesTools.Journal));
    }

    [Fact]
    public void Write_creates_folders_and_reads_back()
    {
        var written = Write("strategy/learned.md", "# Lessons\n- Take Inflame. ⚔\n");
        Assert.False(written.IsError);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount("# Lessons\n- Take Inflame. ⚔\n"), written.Payload["bytes"]!.GetValue<int>());

        var read = Call("notes_read", new JsonObject { ["file"] = "strategy/learned.md" });
        Assert.True(read.Payload["exists"]!.GetValue<bool>());
        Assert.Equal("# Lessons\n- Take Inflame. ⚔\n", read.Payload["content"]!.GetValue<string>());
        Assert.Equal(["learned.md"], Directory.GetFiles(Path.Combine(_dir, "strategy")).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData("../runs/current.md")]
    [InlineData("runs/../runs/current.md")]
    [InlineData("./runs/current.md")]
    [InlineData("/etc/passwd")]
    [InlineData("runs/other.md")]
    [InlineData("RUNS/current.md")]
    [InlineData("runs\\current.md")]
    [InlineData("strategy/general.md")]
    [InlineData(" runs/current.md")]
    [InlineData("")]
    public void Other_paths_are_refused(string file)
    {
        foreach (var outcome in new[] { Write(file, "x"), Call("notes_read", new JsonObject { ["file"] = file }) })
        {
            Assert.True(outcome.IsError);
            Assert.Contains("runs/current.md, strategy/learned.md", outcome.Payload["error"]!.GetValue<string>());
        }
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void Missing_or_wrong_arguments_are_errors()
    {
        Assert.True(Call("notes_read", new JsonObject()).IsError);
        Assert.True(Call("notes_read", new JsonObject { ["file"] = 3 }).IsError);
        Assert.True(Call("notes_write", new JsonObject { ["file"] = "runs/current.md" }).IsError);
        Assert.False(File.Exists(Path.Combine(_dir, "runs", "current.md")));
    }

    [Fact]
    public void Writes_over_the_limit_are_refused()
    {
        Assert.False(Write("runs/current.md", new string('a', NotesTools.MaxBytes)).IsError);
        var tooBig = Write("runs/current.md", new string('b', NotesTools.MaxBytes + 1));
        Assert.True(tooBig.IsError);
        Assert.Equal('a', File.ReadAllText(Path.Combine(_dir, "runs", "current.md"))[0]);
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "runs")));
    }

    [Fact]
    public void A_journal_from_another_run_is_archived_before_it_is_replaced()
    {
        string Archived(ToolOutcome o) => o.Payload["archived_previous_as"]?.GetValue<string>() ?? "";
        string Archive(string name) => File.ReadAllText(Path.Combine(_dir, "runs", name));

        Assert.Equal("", Archived(Write("runs/current.md", "# Ironclad, started 2026-10-08, Neow: gold\nfloor 1")));
        Assert.Equal("", Archived(Write("runs/current.md", "# Ironclad, started 2026-10-08, Neow: gold\nfloor 1\nfloor 2")));

        // A new header is a new run, even for the same character on the same day.
        Assert.Equal("runs/2026-10-08-ironclad.md", Archived(Write("runs/current.md", "# Ironclad, started 2026-10-08, Neow: relic\n")));
        Assert.Equal("# Ironclad, started 2026-10-08, Neow: gold\nfloor 1\nfloor 2", Archive("2026-10-08-ironclad.md"));

        // An archive is never overwritten.
        Assert.Equal("runs/2026-10-08-ironclad-2.md", Archived(Write("runs/current.md", "# Silent, started 2026-10-10, Neow: card\n")));
        Assert.Equal("# Ironclad, started 2026-10-08, Neow: relic\n", Archive("2026-10-08-ironclad-2.md"));
        Assert.Equal("runs/2026-10-10-silent.md", Archived(Write("runs/current.md", "scratch without a header")));

        // Without a header the archive is named after today.
        Assert.Equal("runs/2026-10-10-previous.md", Archived(Write("runs/current.md", "# Defect, started 2026-10-10, Neow: orb\n")));
        Assert.Equal(5, Directory.GetFiles(Path.Combine(_dir, "runs")).Length);

        // The lessons file is never archived.
        Write("strategy/learned.md", "# A");
        Assert.Equal("", Archived(Write("strategy/learned.md", "# B")));
    }

    [Fact]
    public void A_finished_journal_is_archived_even_when_the_new_header_matches()
    {
        const string header = "# Ironclad, started 2026-10-10, Neow: gold";
        Write("runs/current.md", $"{header}\nfloor 1\n");
        // Writing the post-mortem at the end of a run keeps the same journal.
        Assert.Null(Write("runs/current.md", $"{header}\nfloor 1\n\n## Post-mortem\nDied to the Hexaghost.\n").Payload["archived_previous_as"]);

        var next = Write("runs/current.md", $"{header}\n\n## Build plan\nStrength.\n");

        Assert.Equal("runs/2026-10-10-ironclad.md", next.Payload["archived_previous_as"]!.GetValue<string>());
        Assert.Contains("Died to the Hexaghost.", File.ReadAllText(Path.Combine(_dir, "runs", "2026-10-10-ironclad.md")));
    }

    [Fact]
    public void Append_adds_a_line_without_resending_the_file()
    {
        string Journal() => File.ReadAllText(Path.Combine(_dir, "runs", "current.md"));
        ToolOutcome Append(string content) => Call("notes_append", new JsonObject { ["file"] = "runs/current.md", ["content"] = content });

        var first = Append("# Ironclad, started 2026-10-10, Neow: gold");
        Assert.False(first.IsError);
        Assert.True(first.Payload["appended"]!.GetValue<bool>());
        Assert.Equal("# Ironclad, started 2026-10-10, Neow: gold\n", Journal());

        Write("runs/current.md", "# Ironclad, started 2026-10-10, Neow: gold\n## Log\nF1 Jaw Worm, 70 HP");
        Assert.False(Append("F2 Cultist, 64 HP\n").IsError);
        Assert.False(Append("F3 shop").IsError);
        Assert.Equal("# Ironclad, started 2026-10-10, Neow: gold\n## Log\nF1 Jaw Worm, 70 HP\nF2 Cultist, 64 HP\nF3 shop\n", Journal());

        // An appended line never archives, whatever it looks like.
        var odd = Append("# Silent, started 2026-10-11, Neow: card");
        Assert.Null(odd.Payload["archived_previous_as"]);
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "runs")));

        Assert.True(Append("  ").IsError);
        Assert.True(Call("notes_append", new JsonObject { ["file"] = "strategy/general.md", ["content"] = "x" }).IsError);
    }

    [Fact]
    public void Append_past_the_limit_is_refused()
    {
        Write("strategy/learned.md", new string('a', NotesTools.MaxBytes - 10));
        var outcome = Call("notes_append", new JsonObject { ["file"] = "strategy/learned.md", ["content"] = new string('b', 20) });
        Assert.True(outcome.IsError);
        Assert.Contains("notes_write", outcome.Payload["error"]!.GetValue<string>());
        Assert.Equal(NotesTools.MaxBytes - 10, File.ReadAllText(Path.Combine(_dir, "strategy", "learned.md")).Length);
    }

    [Fact]
    public void Specs_are_strict_ready()
    {
        Assert.Equal(["notes_read", "notes_write", "notes_append"], _notes.Specs.Select(s => s.Name));
        Assert.All(_notes.Specs, s => Assert.True(_notes.Handles(s.Name)));
        foreach (var spec in _notes.Specs.Skip(1))
        {
            var file = spec.InputSchema["properties"]!["file"]!;
            Assert.Equal(["runs/current.md", "strategy/learned.md"], file["enum"]!.AsArray().Select(e => e!.GetValue<string>()));
            Assert.Equal(["file", "content"], spec.InputSchema["required"]!.AsArray().Select(e => e!.GetValue<string>()));
        }
        Assert.Contains("32 KB", _notes.Specs[1].Description);
    }
}

public class TranscriptTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-transcript-" + Guid.NewGuid().ToString("N"));

    public TranscriptTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    [Fact]
    public void Appends_json_lines_to_the_days_file()
    {
        var now = new DateTime(2026, 10, 10, 23, 59, 0, DateTimeKind.Utc);
        var transcript = new Transcript(_dir, clock: () => now);
        var entry = new JsonObject { ["turn"] = 0, ["content"] = new JsonArray("a") };
        transcript.Append(entry);
        now = now.AddMinutes(2);
        transcript.Append(new JsonObject { ["turn"] = 1, ["at"] = "given" });

        var first = File.ReadAllLines(Path.Combine(_dir, "transcripts", "2026-10-10.jsonl"));
        var line = JsonNode.Parse(Assert.Single(first))!.AsObject();
        Assert.Equal(["at", "turn", "content"], line.Select(p => p.Key));
        Assert.Equal("2026-10-10T23:59:00.0000000Z", line["at"]!.GetValue<string>());
        Assert.Equal(2, entry.Count);

        var second = File.ReadAllLines(Path.Combine(_dir, "transcripts", "2026-10-11.jsonl"));
        Assert.Equal("given", JsonNode.Parse(Assert.Single(second))!["at"]!.GetValue<string>());
        Assert.Equal(Path.Combine(_dir, "transcripts", "2026-10-11.jsonl"), transcript.CurrentPath);
    }

    [Fact]
    public void Write_failures_are_logged_not_thrown()
    {
        File.WriteAllText(Path.Combine(_dir, "transcripts"), "a file where the folder should be");
        var logged = new List<string>();
        var transcript = new Transcript(_dir, logged.Add);

        transcript.Append(new JsonObject { ["turn"] = 0 });
        transcript.Append(new JsonObject { ["turn"] = 1 });

        Assert.Contains("couldn't write the transcript", Assert.Single(logged));
    }
}

/// <summary>An ApiBrain wired to fakes: scripted API responses, a scripted game, a temp folder,
/// a fixed clock and delays that return at once.</summary>
file sealed class Rig : IDisposable
{
    public const string PromptMarker = "PROMPT-MARKER-7f3a";
    public const string FakeKey = "sk-ant-api03-test-key";

    public static IReadOnlyList<ToolSpec> DefaultSpecs { get; } =
    [
        new("get_state", "State", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        new("wait_for_decision", "Wait", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["timeout_seconds"] = new JsonObject
                {
                    ["type"] = "integer", ["description"] = "Seconds", ["minimum"] = 1, ["maximum"] = 240, ["default"] = 60,
                },
            },
        }),
        new("act", "Act", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["action_id"] = new JsonObject { ["type"] = "string" },
                ["version"] = new JsonObject { ["type"] = "integer" },
            },
            ["required"] = new JsonArray("action_id"),
        }),
    ];

    private readonly ConcurrentQueue<string> _announced = new();
    private readonly ConcurrentQueue<string> _logged = new();
    private readonly ConcurrentQueue<TimeSpan> _delays = new();
    private ApiBrain? _brain;

    public Rig()
    {
        Directory.CreateDirectory(Dir);
        WriteFile(".claude/commands/play.md", $"---\ndescription: test\n---\nPlay {PromptMarker}: $ARGUMENTS\n");
        WriteFile("strategy/general.md", "General notes.");
        Host = new FakeHost(Api);
        Settings = new BrainSettings { ConfigDir = Dir, Effort = "high", MonthlyBudgetUsd = 100m, ApiKey = FakeKey };
        Spend = new SpendTracker(Dir);
        Notes = new NotesTools(Dir);
    }

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "spiremind-brain-" + Guid.NewGuid().ToString("N"));
    public FakeApi Api { get; } = new();
    public FakeHost Host { get; }
    public BrainSettings Settings { get; set; }
    public SpendTracker Spend { get; }
    public NotesTools Notes { get; }
    public string? Character { get; set; } = "Ironclad";
    public List<string> Chat { get; set; } = [];
    public bool UseTranscript { get; init; }
    public IToolHost? Tools { get; init; }
    public bool AnnounceThrows { get; init; }
    public bool LogThrows { get; init; }
    public DateTime Now { get; set; } = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    public TaskCompletionSource<(BrainStopReason Reason, string Message)> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool StoppedWhileRunning { get; private set; }

    public string[] Announced => _announced.ToArray();
    public string[] Logged => _logged.ToArray();
    public TimeSpan[] Delays => _delays.ToArray();

    public ApiBrain Brain => _brain ??= Build();

    public void WriteFile(string relative, string text)
    {
        var path = Path.Combine(Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private ApiBrain Build()
    {
        var brain = new ApiBrain(Api, Tools ?? Host, Notes, Dir, () => Settings, Spend,
            character: () => Character,
            transcript: UseTranscript ? new Transcript(Dir, _logged.Enqueue, () => Now) : null,
            recentChat: () => Chat,
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
            delay: async (t, ct) =>
            {
                _delays.Enqueue(t);
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
            });
        brain.Stopped += (reason, message) =>
        {
            StoppedWhileRunning = brain.IsRunning;
            Stopped.TrySetResult((reason, message));
        };
        return brain;
    }

    public void Dispose()
    {
        try { _brain?.StopAsync().Wait(TimeSpan.FromSeconds(5)); }
        catch (Exception) { /* the test already failed */ }
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}

/// <summary>Scripted Messages API. Once the script runs out, a call records its request,
/// completes <see cref="Exhausted"/> and waits until it is cancelled.</summary>
file sealed class FakeApi : IClaudeApi
{
    private readonly object _gate = new();
    private readonly List<JsonObject> _requests = [];
    private readonly Queue<Func<ClaudeResponse>> _script = new();

    public TaskCompletionSource Exhausted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Count
    {
        get { lock (_gate) return _requests.Count; }
    }

    public JsonObject this[int index]
    {
        get { lock (_gate) return _requests[index]; }
    }

    public FakeApi Then(ClaudeResponse response)
    {
        lock (_gate) _script.Enqueue(() => response);
        return this;
    }

    public FakeApi Then(Exception error)
    {
        lock (_gate) _script.Enqueue(() => throw error);
        return this;
    }

    public async Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct)
    {
        Func<ClaudeResponse>? next;
        lock (_gate)
        {
            _requests.Add((JsonObject)request.DeepClone());
            next = _script.Count > 0 ? _script.Dequeue() : null;
        }
        if (next is null)
        {
            Exhausted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
        return next!();
    }
}

file sealed record HostCall(string Name, JsonObject Args, int ApiCallsBefore);

/// <summary>Scripted game tools. Every call is recorded with how many API requests came before it.</summary>
file sealed class FakeHost(FakeApi api) : IToolHost
{
    private readonly ConcurrentQueue<HostCall> _calls = new();

    public IReadOnlyList<ToolSpec> Specs { get; set; } = Rig.DefaultSpecs;

    public Func<string, JsonObject, CancellationToken, Task<ToolOutcome>> Handler { get; set; } =
        (_, _, _) => Task.FromResult(ApiBrainTests.Outcome(ApiBrainTests.Snap("combat", true)));

    public HostCall[] Calls => _calls.ToArray();

    public Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        _calls.Enqueue(new HostCall(name, (JsonObject)args.DeepClone(), api.Count));
        return Handler(name, args, ct);
    }
}
