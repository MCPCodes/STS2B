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
        Assert.Contains($"Today is {ApiBrain.Today(rig.Now):yyyy-MM-dd}.", opening);
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
    public async Task A_wait_repeated_on_the_same_decision_is_held_locally()
    {
        using var rig = new Rig();
        var waits = 0;
        // The decision stays at version 7 for 50 local waits, then the game moves on to version 8.
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "wait_for_decision"
            ? Outcome(Snap("combat", true, version: ++waits <= 50 ? 7 : 8))
            : Outcome(Snap("combat", true, version: 7)));
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_a", "wait_for_decision")))
            .Then(Reply("tool_use", null, ToolUse("toolu_b", "wait_for_decision")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        // The first wait hands version 7 over at once; the second is held until version 8.
        Assert.Equal(3, rig.Api.Count);
        Assert.Equal(51, waits);
        var second = JsonNode.Parse(rig.Api[2]["messages"]![4]!["content"]![0]!["content"]!.GetValue<string>())!;
        Assert.Equal(8, second["version"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_held_wait_gives_the_same_decision_back_after_the_hold()
    {
        using var rig = new Rig();
        var waits = 0;
        rig.Host.Handler = (name, _, _) =>
        {
            if (name == "wait_for_decision") waits++;
            return Task.FromResult(Outcome(Snap("combat", true, version: 7)));
        };
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_a", "wait_for_decision")))
            .Then(Reply("tool_use", null, ToolUse("toolu_b", "wait_for_decision")))
            .Then(Reply("tool_use", null, ToolUse("toolu_c", "act", new JsonObject { ["action_id"] = "end_turn", ["version"] = 7 })))
            .Then(Reply("tool_use", null, ToolUse("toolu_d", "wait_for_decision")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        // 1 (first wait) + 61 (held for 60 one-second pauses) + 1 (after an act the next wait isn't a repeat).
        Assert.Equal(63, waits);
        Assert.Equal(60, rig.Delays.Count(d => d == TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task The_wait_the_nudge_asks_for_is_not_held()
    {
        using var rig = new Rig();
        var waits = 0;
        rig.Host.Handler = (name, _, _) =>
        {
            if (name == "wait_for_decision") waits++;
            return Task.FromResult(Outcome(Snap("combat", true, version: 7)));
        };
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_a", "wait_for_decision")))
            .Then(Reply("end_turn", null, new JsonObject { ["type"] = "text", ["text"] = "Thinking about Bash." }))
            .Then(Reply("tool_use", null, ToolUse("toolu_b", "wait_for_decision")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(2, waits);
        Assert.DoesNotContain(rig.Delays, d => d == TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Chat_from_a_tool_result_also_goes_in_a_text_block_after_the_results()
    {
        using var rig = new Rig();
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "act"
            ? Outcome(WithChat(Snap("combat", true, version: 8), "Ironclad, take the elite"))
            : Outcome(Snap("combat", true, version: 7)));
        rig.Api.Then(Reply("tool_use", null,
            ToolUse("toolu_1", "act", new JsonObject { ["action_id"] = "play:0>e0", ["version"] = 7 }),
            ToolUse("toolu_2", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var blocks = rig.Api[1]["messages"]![2]!["content"]!.AsArray();
        Assert.Equal(["tool_result", "tool_result", "text"], blocks.Select(b => b!["type"]!.GetValue<string>()));
        Assert.Contains("Ironclad, take the elite", blocks[2]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_blocked_brain_sends_nothing_until_unblocked()
    {
        using var rig = new Rig();
        rig.BlockedReason = InstanceRole.HumanCoopLine;
        rig.Api.Then(Reply("end_turn", null, TextBlock("Done.")));

        rig.Brain.Start();
        await WaitFor(() => rig.Delays.Count(d => d == rig.Brain.IdlePollInterval) >= 3);
        Assert.Equal(0, rig.Api.Count);
        Assert.True(rig.Brain.IsRunning);
        Assert.Single(rig.Logged, l => l == $"brain: holding back: {InstanceRole.HumanCoopLine}");

        rig.BlockedReason = null;
        await rig.Api.Exhausted.Task.WaitAsync(Guard);
        Assert.Equal(2, rig.Api.Count);
    }

    [Fact]
    public async Task An_act_is_refused_once_blocked()
    {
        using var rig = new Rig();
        rig.Api
            .Then(() =>
            {
                // A co-op run started while the request was out.
                rig.BlockedReason = InstanceRole.HumanCoopNoBotsLine;
                return Reply("tool_use", null, ToolUse("toolu_a", "act", new JsonObject { ["action_id"] = "play:0", ["version"] = 1 }));
            });

        rig.Brain.Start();
        await WaitFor(() => rig.Delays.Count(d => d == rig.Brain.IdlePollInterval) >= 3);
        Assert.DoesNotContain(rig.Host.Calls, c => c.Name == "act");
        Assert.Equal(1, rig.Api.Count);

        rig.BlockedReason = null;
        await rig.Api.Exhausted.Task.WaitAsync(Guard);
        var result = rig.Api[1]["messages"]!.AsArray()[^1]!["content"]![0]!;
        Assert.True(result["is_error"]!.GetValue<bool>());
        Assert.Contains($"Not run: {InstanceRole.HumanCoopNoBotsLine}", result["content"]!.GetValue<string>());
        Assert.DoesNotContain(rig.Host.Calls, c => c.Name == "act");
    }

    [Fact]
    public async Task Teammate_lines_from_a_local_wait_reach_the_model_after_an_ended_turn()
    {
        using var rig = new Rig();
        var waits = 0;
        rig.Host.Handler = (name, _, _) => Task.FromResult(name switch
        {
            "wait_for_decision" => ++waits == 1
                ? Outcome(WithTeammate(Snap("combat", false, version: 4), "Defect", "Silent, I'll take the poison card"))
                : Outcome(Snap("combat", true, version: 5)),
            _ => Outcome(Snap("combat", false, version: 3)),
        });
        rig.Api.Then(Reply("end_turn", null, TextBlock("Waiting for my teammates.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(2, rig.Host.Calls.Count(c => c.Name == "wait_for_decision"));
        var blocks = rig.Api[1]["messages"]!.AsArray()[^1]!["content"]!.AsArray();
        Assert.Equal(2, blocks.Count);
        Assert.StartsWith(ApiBrain.KeepPlayingWithState, blocks[0]!["text"]!.GetValue<string>());
        Assert.Contains("\"version\":5", blocks[0]!["text"]!.GetValue<string>());
        var dropped = blocks[1]!["text"]!.GetValue<string>();
        Assert.StartsWith("Earlier lines in the game chat while you waited", dropped);
        Assert.Contains("Silent, I'll take the poison card", dropped);
        Assert.Contains("\"teammate\":true", dropped);
    }

    [Fact]
    public async Task Teammate_lines_from_idling_outside_a_run_reach_the_model()
    {
        using var rig = new Rig();
        var states = 0;
        rig.Host.Handler = (name, _, _) => Task.FromResult(name switch
        {
            "get_state" => ++states switch
            {
                1 => Outcome(Snap("combat", true)),
                2 => Outcome(Snap("game_over", true)),
                3 => Outcome(WithTeammate(Snap("menu", false), "Defect", "Good game, everyone")),
                4 => Outcome(Snap("menu", false)),
                _ => Outcome(WithChat(Snap("menu", false), "One more?")),
            },
            _ => Outcome(Snap("menu", false)),
        });
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")))
            .Then(Reply("end_turn", null, TextBlock("Run over.")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var blocks = rig.Api[2]["messages"]!.AsArray()[^1]!["content"]!.AsArray();
        Assert.Equal(2, blocks.Count);
        Assert.Contains("One more?", blocks[0]!["text"]!.GetValue<string>());
        var dropped = blocks[1]!["text"]!.GetValue<string>();
        Assert.Contains("Good game, everyone", dropped);
        Assert.DoesNotContain("One more?", dropped);
    }

    /// <summary>A tool result with another bot's line, as party chat relays it.</summary>
    private static JsonObject WithTeammate(JsonObject payload, string from, string text)
    {
        payload["chat"] = new JsonObject
        {
            ["you_are"] = "Silent",
            ["messages"] = new JsonArray(new JsonObject { ["from"] = from, ["text"] = text, ["teammate"] = true, ["seconds_ago"] = 0 }),
        };
        return payload;
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
    [InlineData("combat", 151_000, false)] // in a fight the refresh waits
    [InlineData("combat", 299_000, false)]
    [InlineData("combat", 301_000, true)]
    [InlineData("hand_select", 200_000, false)]
    [InlineData("card_select_in_combat", 200_000, false)]
    [InlineData("card_select", 151_000, true)]
    [InlineData("rewards", 151_000, true)]
    [InlineData("event", 151_000, true)]
    public async Task Fresh_context_thresholds(string screen, int promptTokens, bool refreshed)
    {
        using var rig = new Rig();
        var snap = screen == "card_select_in_combat" ? Snap("card_select", true) : Snap(screen, true);
        if (screen == "card_select_in_combat")
            snap["state"] = new JsonObject { ["combat"] = new JsonObject { ["turn"] = 2 } };
        rig.Host.Handler = (_, _, _) => Task.FromResult(Outcome(snap.DeepClone()));
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
    public async Task A_refresh_put_off_in_a_fight_happens_on_the_first_screen_after_it()
    {
        using var rig = new Rig();
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "act"
            ? new ToolOutcome(new JsonObject { ["done"] = "end_turn", ["next"] = Snap("rewards", true, version: 9) })
            : Outcome(Snap("combat", true)));
        rig.Api
            .Then(Reply("tool_use", Usage(input: 5000), ToolUse("toolu_1", "get_state")))
            .Then(Reply("tool_use", Usage(input: 2000, cacheRead: 198_000), ToolUse("toolu_2", "get_state")))
            .Then(Reply("tool_use", Usage(input: 2000, cacheRead: 208_000), ToolUse("toolu_3", "act", new JsonObject { ["action_id"] = "end_turn" })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        // Still in the fight at 200k and 210k: the conversation goes on.
        Assert.Equal(5, rig.Api[2]["messages"]!.AsArray().Count);
        // The fight ended on the end_turn: the rewards screen starts the fresh conversation.
        var opening = Text(Assert.Single(rig.Api[3]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.Refreshed, opening);
        Assert.Contains("\"screen\":\"rewards\"", opening);
        Assert.Single(rig.Logged, l => l.Contains("waits until the fight is over"));
    }

    [Fact]
    public async Task Each_fight_gets_one_line_in_the_fight_log()
    {
        using var rig = new Rig { UseFightLog = true };
        var board = Snap("combat", true, version: 5);
        board["state"] = JsonNode.Parse("""
            {"run": {"act": 1, "floor": 3, "ascension": 0, "room": "Monster"}, "turn": 1, "round": 1,
             "me": {"name": "The Ironclad", "hp": 75, "max_hp": 80},
             "enemies": [{"i": 0, "name": "Jaw Worm", "hp": 40, "max_hp": 40, "intents": ["attack 11"]}],
             "potions": [null, null, null]}
            """);
        var rewards = Snap("rewards", true, version: 6);
        rewards["state"] = JsonNode.Parse("""{"run": {"act": 1, "floor": 3}, "player": {"hp": 70, "max_hp": 80}, "potions": [null, null, null]}""");
        rig.Host.Handler = (name, _, _) => Task.FromResult(name == "act"
            ? new ToolOutcome(new JsonObject { ["done"] = "end_turn", ["next"] = rewards.DeepClone() })
            : Outcome(board.DeepClone()));
        var usage1 = Usage(input: 3000, output: 400);
        var usage2 = Usage(input: 200, output: 100, cacheRead: 3000);
        rig.Api
            .Then(Reply("tool_use", usage1, ToolUse("toolu_1", "get_state")))
            .Then(Reply("tool_use", usage2, ToolUse("toolu_2", "act", new JsonObject { ["action_id"] = "end_turn" })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var line = JsonNode.Parse(Assert.Single(File.ReadAllLines(Path.Combine(rig.Dir, FightLog.FileName))))!;
        Assert.Equal("2026-10-10 A1 F3", line["id"]!.GetValue<string>());
        Assert.Equal("Jaw Worm", Assert.Single(line["enemies"]!.AsArray())!.GetValue<string>());
        Assert.Equal(75, line["hp_before"]!.GetValue<int>());
        Assert.Equal(70, line["hp_after"]!.GetValue<int>());
        Assert.Equal("high", Assert.Single(line["effort"]!.AsArray())!.GetValue<string>());
        // Both responses came in the fight: the opening state was already the board.
        Assert.Equal(2, line["requests"]!.GetValue<int>());
        var spent = ModelPrices.Cost("claude-opus-5-5", usage1).Usd + ModelPrices.Cost("claude-opus-5-5", usage2).Usd;
        Assert.Equal(Math.Round(spent, 4), line["spent_usd"]!.GetValue<decimal>());
        Assert.Equal(0, line["refreshes"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_full_context_window_in_a_fight_still_refreshes_at_once()
    {
        using var rig = new Rig();
        rig.Host.Handler = (_, _, _) => Task.FromResult(Outcome(Snap("hand_select", true)));
        rig.Api
            .Then(Reply("tool_use", Usage(input: 5000), ToolUse("toolu_1", "get_state")))
            .Then(Reply("model_context_window_exceeded", Usage(input: 2000, cacheRead: 198_000)));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.StartsWith(ApiBrain.Refreshed, Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!));
    }

    [Theory]
    [InlineData("combat", null, true)]
    [InlineData("hand_select", null, true)]
    [InlineData("card_select", true, true)]
    [InlineData("card_select", false, false)]
    [InlineData("rewards", null, false)]
    [InlineData("modal", null, false)]
    public void In_fight_screens(string screen, bool? combatSummary, bool inFight)
    {
        var snap = Snap(screen, true);
        if (combatSummary == true)
            snap["state"] = new JsonObject { ["combat"] = new JsonObject() };
        Assert.Equal(inFight, ApiBrain.InFight(snap));
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

    // ---- Elite and boss effort ----

    /// <summary>A snapshot whose state names its room (run.room, as combat snapshots do).</summary>
    private static JsonObject RoomSnap(string screen, string room, long version = 1)
    {
        var snap = Snap(screen, true, version);
        snap["state"] = new JsonObject { ["run"] = new JsonObject { ["floor"] = 6, ["room"] = room } };
        return snap;
    }

    /// <summary>Host whose act calls return <paramref name="afterActs"/> in turn; get_state gives the map.</summary>
    private static Func<string, JsonObject, CancellationToken, Task<ToolOutcome>> ActsReturn(params JsonObject[] afterActs)
    {
        var acts = 0;
        return (name, _, _) => Task.FromResult(Outcome(name == "act"
            ? afterActs[Math.Min(acts++, afterActs.Length - 1)].DeepClone()
            : Snap("map", true)));
    }

    private static ClaudeResponse Act(string id) => Reply("tool_use", null, ToolUse(id, "act", new JsonObject { ["action_id"] = "map:0" }));

    private static string[] Roles(JsonObject request) =>
        request["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()).ToArray();

    private static string? Effort(JsonObject request) => request["output_config"]?["effort"]?.GetValue<string>();

    [Fact]
    public async Task An_elite_room_starts_a_fresh_conversation_at_its_effort_and_the_map_lowers_it_inside_it()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "medium" };
        rig.Host.Handler = ActsReturn(RoomSnap("combat", "Elite"), Snap("rewards", true), Snap("card_reward", true), Snap("map", true));
        rig.Api.Then(Act("toolu_1")).Then(Act("toolu_2")).Then(Act("toolu_3")).Then(Act("toolu_4"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        // Before: the normal effort, no effort messages, no beta.
        Assert.Equal("medium", Effort(rig.Api[0]));
        Assert.Equal(16000, rig.Api[0]["max_tokens"]!.GetValue<int>());
        Assert.Null(rig.Api[0][ClaudeClient.BetasField]);

        // Entering the elite room raises the effort: a fresh conversation at high from its first
        // request, as the benchmark played each fight; max_tokens follows the effort in effect.
        var text = Text(Assert.Single(rig.Api[1]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.RoomEffortChanged, text);
        Assert.Contains("\"room\":\"Elite\"", text);
        Assert.Equal("high", Effort(rig.Api[1]));
        Assert.Equal(32000, rig.Api[1]["max_tokens"]!.GetValue<int>());
        Assert.Null(rig.Api[1][ClaudeClient.BetasField]);

        // The room's own screens (rewards, card reward) keep the elite effort.
        Assert.Equal(["user", "assistant", "user"], Roles(rig.Api[2]));
        Assert.Equal("high", Effort(rig.Api[2]));
        Assert.Equal(32000, rig.Api[2]["max_tokens"]!.GetValue<int>());
        Assert.Equal(32000, rig.Api[3]["max_tokens"]!.GetValue<int>());

        // The map lowers it inside the conversation: an effort-only system message before the new
        // user turn, which gets a note after its tool result so it counts as new input.
        Assert.Equal(["user", "assistant", "user", "assistant", "user", "assistant", "system", "user"], Roles(rig.Api[4]));
        var lower = rig.Api[4]["messages"]![6]!;
        Assert.Empty(lower["content"]!.AsArray());
        Assert.Equal("medium", lower["output_config"]!["effort"]!.GetValue<string>());
        var turn = rig.Api[4]["messages"]![7]!["content"]!.AsArray();
        Assert.Equal(["tool_result", "text"], turn.Select(b => b!["type"]!.GetValue<string>()).ToArray());
        Assert.Equal("(You left the Elite room, so the thinking effort is back to medium.)", turn[1]!["text"]!.GetValue<string>());
        // The top-level effort and the earlier messages stay as they were, so the cache holds.
        Assert.Equal("high", Effort(rig.Api[4]));
        for (var i = 0; i < 5; i++)
            Assert.Equal(rig.Api[3]["messages"]![i]!.ToJsonString(), rig.Api[4]["messages"]![i]!.ToJsonString());
        Assert.Equal(16000, rig.Api[4]["max_tokens"]!.GetValue<int>());
        Assert.Equal(ApiBrain.PerMessageEffortBeta, Assert.Single(rig.Api[4][ClaudeClient.BetasField]!.AsArray())!.GetValue<string>());

        // Each switch is logged once.
        Assert.Single(rig.Logged, l => l.Contains("entered the Elite room; effort medium to high, starting a fresh conversation"));
        Assert.Single(rig.Logged, l => l.Contains("left the Elite room; effort high to medium, keeping the conversation"));
    }

    [Fact]
    public async Task An_elite_effort_below_the_normal_one_is_lowered_inside_the_conversation_and_leaving_starts_afresh()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "high", EliteEffort = "medium" };
        rig.Host.Handler = ActsReturn(RoomSnap("combat", "Elite"), Snap("map", true));
        rig.Api.Then(Act("toolu_1")).Then(Act("toolu_2"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(["user", "assistant", "system", "user"], Roles(rig.Api[1]));
        Assert.Equal("medium", rig.Api[1]["messages"]![2]!["output_config"]!["effort"]!.GetValue<string>());
        Assert.Equal("(Elite rooms are played at medium effort, so the thinking effort is now medium.)",
            rig.Api[1]["messages"]![3]!["content"]!.AsArray()[^1]!["text"]!.GetValue<string>());
        Assert.Equal("high", Effort(rig.Api[1]));
        Assert.Equal(16000, rig.Api[1]["max_tokens"]!.GetValue<int>());

        // Back up to the normal effort on the map: a fresh conversation.
        Assert.StartsWith(ApiBrain.RoomEffortChanged, Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!));
        Assert.Equal("high", Effort(rig.Api[2]));
        Assert.Null(rig.Api[2][ClaudeClient.BetasField]);
    }

    [Fact]
    public async Task A_boss_room_uses_the_boss_effort_and_a_fight_effort_like_the_normal_one_changes_nothing()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "medium", EliteEffort = "same", BossEffort = "max" };
        rig.Host.Handler = ActsReturn(RoomSnap("combat", "Elite"), Snap("map", true), RoomSnap("combat", "Boss"));
        rig.Api.Then(Act("toolu_1")).Then(Act("toolu_2")).Then(Act("toolu_3"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(["user", "assistant", "user"], Roles(rig.Api[1])); // elite: same as the normal effort
        Assert.Equal(["user", "assistant", "user", "assistant", "user"], Roles(rig.Api[2]));
        Assert.StartsWith(ApiBrain.RoomEffortChanged, Text(Assert.Single(rig.Api[3]["messages"]!.AsArray())!));
        Assert.Equal("max", Effort(rig.Api[3]));
        Assert.Equal(64000, rig.Api[3]["max_tokens"]!.GetValue<int>());
    }

    [Fact]
    public async Task Starting_in_a_boss_fight_puts_the_boss_effort_at_the_top_level()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "low" };
        rig.Host.Handler = (_, _, _) => Task.FromResult(Outcome(RoomSnap("combat", "Boss")));
        rig.Api.Then(Act("toolu_1"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal("high", Effort(rig.Api[0]));
        Assert.Equal(32000, rig.Api[0]["max_tokens"]!.GetValue<int>());
        Assert.Equal(["user"], Roles(rig.Api[0]));
        Assert.Equal(["user", "assistant", "user"], Roles(rig.Api[1])); // still the boss room, nothing switches
        Assert.Null(rig.Api[1][ClaudeClient.BetasField]);
    }

    [Fact]
    public async Task A_model_without_per_message_effort_lowers_it_with_a_fresh_conversation_too()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Model = "claude-fable-5", Effort = "medium" };
        rig.Host.Handler = ActsReturn(RoomSnap("combat", "Elite"), Snap("map", true));
        rig.Api.Then(Act("toolu_1")).Then(Act("toolu_2"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.StartsWith(ApiBrain.RoomEffortChanged, Text(Assert.Single(rig.Api[1]["messages"]!.AsArray())!));
        Assert.Equal("high", Effort(rig.Api[1]));
        var text = Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.RoomEffortChanged, text);
        Assert.Contains("\"screen\":\"map\"", text);
        Assert.Equal("medium", Effort(rig.Api[2]));
        Assert.Equal(16000, rig.Api[2]["max_tokens"]!.GetValue<int>());
        Assert.Null(rig.Api[2][ClaudeClient.BetasField]);
        Assert.Contains(rig.Logged, l => l.Contains("left the Elite room; effort high to medium, starting a fresh conversation"));
    }

    [Fact]
    public async Task Effort_typed_in_a_fight_room_still_starts_a_fresh_conversation()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "medium" };
        var acts = 0;
        rig.Host.Handler = (name, _, _) =>
        {
            if (name != "act")
                return Task.FromResult(Outcome(Snap("map", true)));
            if (++acts == 2)
                rig.Settings = rig.Settings with { BossEffort = "low" }; // /effort boss low mid-fight
            return Task.FromResult(Outcome(RoomSnap("combat", "Boss")));
        };
        rig.Api.Then(Act("toolu_1")).Then(Act("toolu_2"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.StartsWith(ApiBrain.RoomEffortChanged, Text(Assert.Single(rig.Api[1]["messages"]!.AsArray())!)); // entering
        var text = Text(Assert.Single(rig.Api[2]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.ModelChanged, text); // a human's change, even a lower one
        Assert.Equal("low", Effort(rig.Api[2]));
        Assert.Equal(16000, rig.Api[2]["max_tokens"]!.GetValue<int>());
        Assert.Null(rig.Api[2][ClaudeClient.BetasField]);
    }

    [Fact]
    public async Task Normal_effort_typed_in_a_fight_room_waits_until_the_room_is_left()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "medium", EliteEffort = "high" };
        var acts = 0;
        rig.Host.Handler = (name, _, _) =>
        {
            if (name != "act")
                return Task.FromResult(Outcome(Snap("map", true)));
            acts++;
            if (acts == 2)
                rig.Settings = rig.Settings with { Effort = "low" }; // /effort low in the fight
            return Task.FromResult(Outcome(acts < 3 ? RoomSnap("combat", "Elite") : Snap("map", true)));
        };
        rig.Api.Then(Act("toolu_1")).Then(Act("toolu_2")).Then(Act("toolu_3"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(["user", "assistant", "user"], Roles(rig.Api[2])); // unchanged in the fight
        Assert.Equal(32000, rig.Api[2]["max_tokens"]!.GetValue<int>());
        Assert.Equal(["user", "assistant", "user", "assistant", "system", "user"], Roles(rig.Api[3]));
        Assert.Equal("low", rig.Api[3]["messages"]![4]!["output_config"]!["effort"]!.GetValue<string>());
    }

    [Fact]
    public async Task Normal_effort_typed_as_the_bot_enters_a_fight_room_goes_by_the_room()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "medium" };
        var acts = 0;
        rig.Host.Handler = (name, _, _) =>
        {
            if (name != "act")
                return Task.FromResult(Outcome(Snap("map", true)));
            if (++acts == 1)
                rig.Settings = rig.Settings with { Effort = "low" }; // /effort low as the fight starts
            return Task.FromResult(Outcome(acts == 1 ? RoomSnap("combat", "Elite") : Snap("map", true)));
        };
        rig.Api.Then(Act("toolu_1")).Then(Act("toolu_2"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        // A raise to the elite's high: a fresh conversation, as for the room alone.
        Assert.StartsWith(ApiBrain.RoomEffortChanged, Text(Assert.Single(rig.Api[1]["messages"]!.AsArray())!));
        Assert.Equal("high", Effort(rig.Api[1]));
        // Leaving lowers it to the new normal effort.
        Assert.Equal("low", rig.Api[2]["messages"]!.AsArray()[^2]!["output_config"]!["effort"]!.GetValue<string>());
        Assert.Equal("(You left the Elite room, so the thinking effort is back to low.)",
            rig.Api[2]["messages"]!.AsArray()[^1]!["content"]!.AsArray()[^1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_refused_effort_message_falls_back_to_fresh_conversations()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "medium" };
        rig.Host.Handler = ActsReturn(RoomSnap("combat", "Elite"), Snap("map", true), RoomSnap("combat", "Elite"), Snap("map", true));
        rig.Api
            .Then(Act("toolu_1"))
            .Then(Act("toolu_2"))
            .Then(new ClaudeApiException(ClaudeErrorKind.InvalidRequest, 400, "invalid_request_error", "messages.4: unexpected role", "req_1"))
            .Then(Act("toolu_3"))
            .Then(Act("toolu_4"));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        // The first request with the effort message is refused: a fresh conversation, said once in the chat.
        Assert.Equal("system", Roles(rig.Api[2])[^2]);
        var text = Text(Assert.Single(rig.Api[3]["messages"]!.AsArray())!);
        Assert.StartsWith(ApiBrain.RoomEffortChanged, text);
        Assert.Equal("medium", Effort(rig.Api[3]));
        Assert.Null(rig.Api[3][ClaudeClient.BetasField]);
        Assert.Equal([ApiBrain.PerMessageEffortRefused], rig.Announced);
        // Leaving the next elite room starts afresh too, instead of trying the beta again.
        Assert.Equal("high", Effort(rig.Api[4]));
        Assert.StartsWith(ApiBrain.RoomEffortChanged, Text(Assert.Single(rig.Api[5]["messages"]!.AsArray())!));
        Assert.Equal("medium", Effort(rig.Api[5]));
        Assert.Null(rig.Api[5][ClaudeClient.BetasField]);
        Assert.True(rig.Brain.IsRunning);
    }

    [Theory]
    [InlineData("messages.6.content.0: unexpected tool_use_id found in tool_result blocks", false)]
    [InlineData("messages.4.output_config: Extra inputs are not permitted", true)]
    public async Task A_later_rejection_is_taken_for_the_effort_message_only_when_it_names_it(string apiMessage, bool fallsBack)
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = "medium" };
        rig.Host.Handler = ActsReturn(RoomSnap("combat", "Elite"), Snap("map", true), Snap("map", true));
        rig.Api
            .Then(Act("toolu_1"))
            .Then(Act("toolu_2"))
            .Then(Act("toolu_3")) // the request that carried the effort message first went through
            .Then(new ClaudeApiException(ClaudeErrorKind.InvalidRequest, 400, "invalid_request_error", apiMessage, "req_1"));

        rig.Brain.Start();
        if (fallsBack)
        {
            await rig.Api.Exhausted.Task.WaitAsync(Guard);
            Assert.StartsWith(ApiBrain.RoomEffortChanged, Text(Assert.Single(rig.Api[4]["messages"]!.AsArray())!));
            Assert.Equal([ApiBrain.PerMessageEffortRefused], rig.Announced);
        }
        else
        {
            var (reason, message) = await rig.Stopped.Task.WaitAsync(Guard);
            Assert.Equal(BrainStopReason.Error, reason);
            Assert.StartsWith("Claude rejected the brain's request", message);
        }
    }

    [Theory]
    [InlineData("claude-opus-5-5", true)]
    [InlineData("opus", true)]
    [InlineData("claude-opus-5-5-20261001", true)]
    [InlineData("claude-sonnet-5-5", true)]
    [InlineData("claude-haiku-5-5", true)]
    [InlineData("claude-fable-5-1", true)]
    [InlineData("claude-mythos-5", true)]
    // Thinking is off by default on Opus 4.8 and 4.7, so the field would turn it on there.
    [InlineData("claude-opus-4-8", false)]
    [InlineData("claude-opus-4-7", false)]
    [InlineData("claude-opus-4-8-20260801", false)]
    [InlineData("claude-haiku-4-5", false)]
    [InlineData("claude-opus-4-6", false)]
    [InlineData("claude-opus-5-55", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_models_with_adaptive_thinking_display_take_the_summary(string? model, bool takes) =>
        Assert.Equal(takes, ModelPrices.TakesSummarizedThinking(model));

    [Fact]
    public async Task Other_models_get_no_thinking_field()
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Model = "claude-haiku-4-5" };

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.False(rig.Api[0].ContainsKey("thinking"));
        Assert.Equal("claude-haiku-4-5", rig.Api[0]["model"]!.GetValue<string>());
    }

    [Fact]
    public async Task Thinking_summaries_go_in_the_transcript_and_the_blocks_go_back_unchanged()
    {
        using var rig = new Rig { UseTranscript = true };
        var thinking = Thinking("Jaw Worm hits for 11, so Defend twice before Bash.", "sig/abc+==");
        rig.Api
            .Then(Reply("tool_use", Usage(), Thinking("", "empty+sig=="), thinking.DeepClone().AsObject(), ToolUse("toolu_1", "get_state")))
            .Then(Reply("tool_use", Usage(), ToolUse("toolu_2", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var lines = File.ReadAllLines(Path.Combine(rig.Dir, "transcripts", "2026-10-10.jsonl")).Select(l => JsonNode.Parse(l)!).ToArray();
        Assert.Equal(["Jaw Worm hits for 11, so Defend twice before Bash."], lines[0]["thinking"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.False(lines[1].AsObject().ContainsKey("thinking"));

        var sent = rig.Api[1]["messages"]![1]!["content"]!.AsArray();
        Assert.Equal(Thinking("", "empty+sig==").ToJsonString(), sent[0]!.ToJsonString());
        Assert.Equal(thinking.ToJsonString(), sent[1]!.ToJsonString());
    }

    [Fact]
    public void Only_models_the_beta_lists_take_per_message_effort()
    {
        Assert.True(ApiBrain.SupportsPerMessageEffort("claude-opus-5-5"));
        Assert.True(ApiBrain.SupportsPerMessageEffort("claude-sonnet-5-5"));
        Assert.True(ApiBrain.SupportsPerMessageEffort("claude-haiku-5-5"));
        Assert.True(ApiBrain.SupportsPerMessageEffort("claude-fable-5-1"));
        Assert.False(ApiBrain.SupportsPerMessageEffort("claude-fable-5"));
        Assert.False(ApiBrain.SupportsPerMessageEffort("claude-opus-4-8"));
        Assert.False(ApiBrain.SupportsPerMessageEffort(null));
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
        Assert.Equal([ApiBrain.Overloaded], rig.Announced);
        Assert.True(rig.Brain.IsRunning);
        Assert.False(rig.Stopped.Task.IsCompleted);
    }

    [Fact]
    public async Task A_rate_limit_is_named_in_the_chat_with_the_api_s_own_sentence()
    {
        using var rig = new Rig();
        rig.Api.Then(new ClaudeApiException(ClaudeErrorKind.RateLimited, 429, "rate_limit_error",
            "This request would exceed your organization's rate limit of 8,000 output tokens per minute", "req_1", attempts: 5));
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var line = Assert.Single(rig.Announced);
        Assert.StartsWith(ApiBrain.RateLimited, line);
        Assert.Contains("8,000 output tokens per minute", line);
    }

    [Fact]
    public async Task A_model_change_while_retrying_goes_into_the_next_request()
    {
        using var rig = new Rig();
        rig.Api.Then(() =>
        {
            rig.Settings = rig.Settings with { Model = "haiku", Effort = "low" }; // /model haiku, /effort low while it retries
            throw new ClaudeApiException(ClaudeErrorKind.RateLimited, 429, "rate_limit_error", "slow down", null, attempts: 5);
        });
        rig.Api.Then(new ClaudeApiException(ClaudeErrorKind.RateLimited, 429, "rate_limit_error", "slow down", null, attempts: 5));
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal("claude-opus-5-5", rig.Api[0]["model"]!.GetValue<string>());
        Assert.All(Enumerable.Range(1, rig.Api.Count - 1), i =>
        {
            Assert.Equal("claude-haiku-5-5", rig.Api[i]["model"]!.GetValue<string>());
            Assert.Equal("low", rig.Api[i]["output_config"]!["effort"]!.GetValue<string>());
        });
        Assert.True(rig.Brain.IsRunning);
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
    public async Task Two_refusals_in_a_row_announce_and_stop_with_error()
    {
        using var rig = new Rig();
        rig.Api.Then(Reply("refusal", null)).Then(Reply("refusal", null));

        rig.Brain.Start();
        var (reason, _) = await rig.Stopped.Task.WaitAsync(Guard);

        Assert.Equal(BrainStopReason.Error, reason);
        Assert.Contains(rig.Announced, a => a.Contains("refusal"));
        Assert.Equal(2, rig.Api.Count);
    }

    [Fact]
    public async Task One_refusal_starts_a_fresh_conversation_and_play_goes_on()
    {
        using var rig = new Rig();
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")))
            .Then(Reply("refusal", null))
            .Then(Reply("tool_use", null, ToolUse("toolu_2", "get_state")));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(4, rig.Api.Count);
        Assert.Equal(3, rig.Api[1]["messages"]!.AsArray().Count);
        Assert.Single(rig.Api[2]["messages"]!.AsArray());
        Assert.StartsWith(ApiBrain.Refreshed, Text(rig.Api[2]["messages"]![0]!));
        Assert.Empty(rig.Announced);
        Assert.Contains(rig.Logged, l => l.Contains("refusal") && l.Contains("no category"));
        Assert.True(rig.Brain.IsRunning);
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
        Assert.Equal(32000, request["max_tokens"]!.GetValue<int>()); // high effort
        foreach (var absent in new[] { "temperature", "top_p", "top_k", "tool_choice" })
            Assert.False(request.ContainsKey(absent), absent);
        Assert.Equal("high", request["output_config"]!["effort"]!.GetValue<string>());
        // Thinking is on anyway; the field asks for its summary instead of empty blocks.
        Assert.Equal("""{"type":"adaptive","display":"summarized"}""", request["thinking"]!.ToJsonString());

        var system = Assert.Single(request["system"]!.AsArray())!;
        Assert.Equal("text", system["type"]!.GetValue<string>());
        Assert.Equal("""{"type":"ephemeral","ttl":"1h"}""", system["cache_control"]!.ToJsonString());
        Assert.Equal("""{"type":"ephemeral"}""", request["cache_control"]!.ToJsonString());

        var tools = request["tools"]!.AsArray().Select(t => t!.AsObject()).ToArray();
        Assert.Equal(["get_state", "wait_for_decision", "act", "nested", "notes_read", "notes_write", "notes_append", "notes_list"], tools.Select(t => t["name"]!.GetValue<string>()));
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

    [Theory]
    [InlineData("low", null, 16000)]
    [InlineData("medium", null, 16000)]
    [InlineData("high", null, 32000)]
    [InlineData("xhigh", null, 64000)]
    [InlineData("max", null, 64000)]
    [InlineData("max", 20000, 20000)]
    [InlineData("low", 40000, 40000)]
    public async Task Max_tokens_follow_the_effort_unless_config_sets_them(string effort, int? maxTokens, int expected)
    {
        using var rig = new Rig();
        rig.Settings = rig.Settings with { Effort = effort, MaxTokens = maxTokens };

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        Assert.Equal(expected, rig.Api[0]["max_tokens"]!.GetValue<int>());
        Assert.Equal(effort, rig.Api[0]["output_config"]!["effort"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_brain_tells_whether_it_last_saw_a_run()
    {
        using var rig = new Rig();
        var screen = "menu";
        rig.Host.Handler = (_, _, _) => Task.FromResult(Outcome(Snap(screen, false)));
        Assert.Null(rig.Brain.LastSeenInRun);

        rig.Brain.Start();
        await WaitFor(() => rig.Brain.LastSeenInRun == false);

        // A run starts: the next idle poll sees it and the first request goes out.
        screen = "event";
        await rig.Api.Exhausted.Task.WaitAsync(Guard);
        Assert.True(rig.Brain.LastSeenInRun);
    }

    [Fact]
    public void Today_is_the_players_local_calendar_day()
    {
        var utc = new DateTime(2026, 10, 10, 3, 30, 0, DateTimeKind.Utc);
        Assert.Equal(utc.ToLocalTime().Date, ApiBrain.Today(utc));
        Assert.Equal(utc.ToLocalTime().Date, ApiBrain.Today(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)));
        Assert.Equal(new DateTime(2026, 10, 10), ApiBrain.Today(new DateTime(2026, 10, 10, 23, 59, 0, DateTimeKind.Local)));
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting");
            await Task.Delay(5);
        }
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
            .Then(Reply("tool_use", Usage(input: 2000, cacheRead: 310_000), ToolUse("toolu_2", "get_state")));

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
        rig.Api.Then(Reply("refusal", null)).Then(Reply("refusal", null));

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
        rig.Host.Handler = (name, _, _) =>
        {
            // A lesson accepted by hand at the end of the run (the brain's own are held for review).
            if (++states == 2)
                rig.WriteFile("strategy/learned.md", "NEW-LESSON");
            return Task.FromResult(states switch
            {
                1 => Outcome(Snap("combat", true)),
                2 => Outcome(Snap("game_over", true)),
                3 => Outcome(Snap("menu", false)),
                _ => Outcome(Snap("event", true, marker: "neow")),
            });
        };
        rig.Api
            .Then(Reply("tool_use", null, ToolUse("toolu_1", "get_state")))
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
    public async Task The_wiki_is_indexed_in_the_prompt_and_its_tools_run_in_process()
    {
        using var rig = new Rig();
        rig.WriteFile("strategy/wiki/enemies/jaw-worm.md", "# Jaw Worm\nAn Act 1 brute.\n\n## Facts (game files, v0.107.1)\n- FACT-BODY\n\n## Lessons\n");
        rig.Api.Then(Reply("tool_use", null,
            ToolUse("toolu_1", "notes_list"),
            ToolUse("toolu_2", "notes_append", new JsonObject
            {
                ["file"] = "strategy/wiki/enemies/jaw-worm.md", ["content"] = "- (Ironclad, 2026-10-10, floor 3) NEW-LESSON",
            })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var system = SystemText(rig.Api[0]);
        Assert.Contains("\nstrategy/wiki/enemies/jaw-worm.md: An Act 1 brute.\n", system);
        Assert.DoesNotContain("FACT-BODY", system);

        var results = rig.Api[1]["messages"]![2]!["content"]!.AsArray();
        var listed = JsonNode.Parse(results[0]!["content"]!.GetValue<string>())!;
        Assert.Equal("Jaw Worm", listed["wiki"]![0]!["title"]!.GetValue<string>());
        Assert.False(results[1]!["is_error"]!.GetValue<bool>());
        // The lesson is held for review: the page is unchanged and the line waits in its pending file.
        Assert.True(JsonNode.Parse(results[1]!["content"]!.GetValue<string>())!["held_for_review"]!.GetValue<bool>());
        Assert.EndsWith("## Lessons\n", File.ReadAllText(Path.Combine(rig.Dir, "strategy", "wiki", "enemies", "jaw-worm.md")));
        Assert.EndsWith("- (Ironclad, 2026-10-10, floor 3) NEW-LESSON\n", File.ReadAllText(Path.Combine(rig.Dir, "strategy", "pending", "wiki", "enemies", "jaw-worm.md")));
        Assert.DoesNotContain(rig.Host.Calls, c => c.Name.StartsWith("notes_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plays_through_the_real_bot_tools()
    {
        var game = new FakeGame { ActionDelay = TimeSpan.Zero };
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner) { PollInterval = TimeSpan.FromMilliseconds(1), SettleTime = TimeSpan.Zero };
        var tools = new BotTools(game, waiter, versioner, new ChatHub()) { ActSettleTimeout = TimeSpan.FromSeconds(5) };
        using var rig = new Rig { Tools = new BotToolsHost(tools) };
        var version = (await tools.CallAsync("get_state", new JsonObject(), CancellationToken.None)).Payload["version"]!.GetValue<long>();
        rig.Api.Then(Reply("tool_use", null, ToolUse("toolu_1", "act", new JsonObject { ["action_id"] = "play:0>e0", ["version"] = version })));

        rig.Brain.Start();
        await rig.Api.Exhausted.Task.WaitAsync(Guard);

        var names = rig.Api[0]["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>());
        Assert.Equal(tools.Specs.Select(s => s.Name).Concat(["notes_read", "notes_write", "notes_append", "notes_list"]), names);
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
        Assert.StartsWith("## Wiki index\n\nThe wiki is empty", solo.Text);
        Assert.Contains("\n\n## Playing from inside the game", solo.Text);

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

    [Fact]
    public void The_rules_sheet_follows_the_general_notes_and_is_labelled()
    {
        Write("strategy/general.md", "GENERAL-NOTES");
        Write("strategy/rules.md", "# Rules\nRULES-SHEET\n");
        Write("strategy/bosses-and-elites.md", "BOSS-NOTES");
        Write("strategy/learned.md", "LEARNED-NOTES");

        var prompt = PromptBuilder.Build(_dir, null, multiplayer: false);

        Assert.Contains($"<file path=\"strategy/rules.md\" about=\"{PromptBuilder.RulesLabel}\">\n# Rules\nRULES-SHEET\n</file>", prompt.Text);
        Assert.Contains("<file path=\"strategy/general.md\">\nGENERAL-NOTES\n</file>", prompt.Text);
        var order = new[] { "GENERAL-NOTES", "RULES-SHEET", "BOSS-NOTES", "LEARNED-NOTES" }.Select(t => prompt.Text.IndexOf(t, StringComparison.Ordinal)).ToArray();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Equal(["general.md", "rules.md", "bosses-and-elites.md", "learned.md"], prompt.Files.Select(Path.GetFileName));
    }

    [Fact]
    public void A_missing_rules_sheet_is_skipped()
    {
        Write("strategy/general.md", "GENERAL-NOTES");
        Write("strategy/bosses-and-elites.md", "BOSS-NOTES");

        var prompt = PromptBuilder.Build(_dir, null, multiplayer: false);

        Assert.DoesNotContain("<file path=\"strategy/rules.md\"", prompt.Text);
        Assert.DoesNotContain(PromptBuilder.RulesLabel, prompt.Text);
        Assert.Equal(["general.md", "bosses-and-elites.md"], prompt.Files.Select(Path.GetFileName));
    }

    [Fact]
    public void An_empty_wiki_says_so()
    {
        Write("strategy/wiki/README.md", "# Wiki conventions\nFor humans.\n");

        var prompt = PromptBuilder.Build(_dir, null, multiplayer: false);

        Assert.Contains("## Wiki index\n\nThe wiki is empty: no pages under `strategy/wiki/` yet.\n\n## Playing from inside the game", prompt.Text);
        Assert.DoesNotContain("For humans", prompt.Text);
    }

    [Fact]
    public void The_wiki_index_lists_each_page_with_its_summary_but_not_its_text()
    {
        Write("strategy/general.md", "GENERAL-NOTES");
        Write("strategy/learned.md", "LEARNED-NOTES");
        Write("strategy/wiki/README.md", "# Conventions\nHUMANS-ONLY\n");
        Write("strategy/wiki/potions.md", "# Potions\nEvery potion, with exact numbers.\n\n## Facts (game files, v0.107.1)\n- POTION-FACT\n\n## Lessons\n");
        Write("strategy/wiki/enemies/jaw-worm.md", "# Jaw Worm\nAn Act 1 brute.\n\n## Facts (game files, v0.107.1)\n- WORM-FACT\n");
        Write("strategy/wiki/lessons/no-summary.md", "# No summary\n## Lessons\n- (Ironclad, 2026-10-10) LESSON-LINE\n");
        Write("strategy/wiki/enemies/Bad-Name.md", "# Bad\nNot a page.\n");

        var prompt = PromptBuilder.Build(_dir, null, multiplayer: false);

        Assert.Contains(
            "## Wiki index\n\nPages under `strategy/wiki/` when this prompt was built, with their summaries (`notes_list` also shows newer ones):\n\n" +
            "strategy/wiki/enemies/jaw-worm.md: An Act 1 brute.\n" +
            "strategy/wiki/lessons/no-summary.md\n" +
            "strategy/wiki/potions.md: Every potion, with exact numbers.\n\n" +
            "## Playing from inside the game", prompt.Text);
        foreach (var body in new[] { "HUMANS-ONLY", "POTION-FACT", "WORM-FACT", "LESSON-LINE", "Not a page", "Bad-Name" })
            Assert.DoesNotContain(body, prompt.Text);
        Assert.True(prompt.Text.IndexOf("LEARNED-NOTES", StringComparison.Ordinal) < prompt.Text.IndexOf("## Wiki index", StringComparison.Ordinal));
        Assert.DoesNotContain(prompt.Files, f => f.Contains("wiki", StringComparison.Ordinal));
    }

    [Fact]
    public void The_wiki_index_comes_from_the_notes_folder_when_given()
    {
        Write("strategy/wiki/staged.md", "# Staged\nA copy in the prompt folder.\n");
        var notes = Path.Combine(_dir, "notes");
        Directory.CreateDirectory(Path.Combine(notes, "strategy", "wiki", "lessons"));
        File.WriteAllText(Path.Combine(notes, "strategy", "wiki", "lessons", "written.md"), "# Written\nA page the brain wrote.\n");

        Assert.Contains("strategy/wiki/staged.md: A copy in the prompt folder.", PromptBuilder.Build(_dir, null, false).Text);
        var prompt = PromptBuilder.Build(_dir, null, false, notes);
        Assert.Contains("strategy/wiki/lessons/written.md: A page the brain wrote.", prompt.Text);
        Assert.DoesNotContain("staged.md", prompt.Text);
    }

    [Fact]
    public void The_wiki_index_stops_at_its_limit()
    {
        var count = PromptBuilder.WikiIndexLimit + 5;
        for (var i = 0; i < count; i++)
            Write($"strategy/wiki/lessons/page-{i:D3}.md", $"# Page {i}\nSummary {i}.\n");

        var index = PromptBuilder.WikiIndex(_dir);

        var lines = index.Split('\n').Where(l => l.StartsWith("strategy/wiki/", StringComparison.Ordinal)).ToArray();
        Assert.Equal(PromptBuilder.WikiIndexLimit, lines.Length);
        Assert.Equal("strategy/wiki/lessons/page-000.md: Summary 0.", lines[0]);
        Assert.Equal("strategy/wiki/lessons/page-199.md: Summary 199.", lines[^1]);
        Assert.EndsWith("\nstrategy/wiki/lessons/page-199.md: Summary 199.\n5 more pages; `notes_list` shows them all.", index);
        Assert.Contains(index, PromptBuilder.Build(_dir, null, false).Text);
    }

    [Fact]
    public void The_harness_notes_cover_the_wiki_tools()
    {
        foreach (var text in new[] { "notes_list", "strategy/wiki/<slug>.md", "strategy/wiki/<folder>/<slug>.md", "## Lessons" })
            Assert.Contains(text, PromptBuilder.HarnessNotes);
        // rules.md can be missing, so the notes don't promise it is above.
        Assert.DoesNotContain("strategy/rules.md", PromptBuilder.HarnessNotes);
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
        // These tests cover the files themselves; holding lessons for review is tested in LessonHoldTests.
        _notes = new NotesTools(_dir, () => new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc), new NotesOptions { HoldLessons = false });
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
    public void The_header_line_the_prompt_asks_for_names_the_archive()
    {
        // The API brain can't read runs/README.md, so the format in the harness notes is all it
        // has; a header written that way must give the archive its run's date and character.
        var notes = PromptBuilder.HarnessNotes;
        var start = notes.IndexOf("`# ", StringComparison.Ordinal) + 1;
        var example = notes[start..notes.IndexOf('`', start)];
        Assert.Contains("started YYYY-MM-DD", example);

        Write("runs/current.md", example.Replace("YYYY-MM-DD", "2026-10-09").Replace("<what you picked>", "gold") + "\nfloor 1");
        var archived = Write("runs/current.md", "# Silent, started 2026-10-10, Neow: card\n").Payload["archived_previous_as"]?.GetValue<string>();

        Assert.Equal("runs/2026-10-09-ironclad.md", archived);
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
        Assert.Equal(["notes_read", "notes_write", "notes_append", "notes_list"], _notes.Specs.Select(s => s.Name));
        Assert.All(_notes.Specs, s => Assert.True(_notes.Handles(s.Name)));
        foreach (var spec in _notes.Specs.Take(3))
        {
            // Wiki pages can't be an enum and strict schemas take no pattern, so the forms are described.
            var file = spec.InputSchema["properties"]!["file"]!.AsObject();
            Assert.Equal("string", file["type"]!.GetValue<string>());
            Assert.False(file.ContainsKey("enum"));
            Assert.False(file.ContainsKey("pattern"));
            Assert.Contains(NotesTools.FileForms, file["description"]!.GetValue<string>());
            Assert.Equal(spec.Name == "notes_read" ? ["file"] : ["file", "content"],
                spec.InputSchema["required"]!.AsArray().Select(e => e!.GetValue<string>()));
        }
        Assert.Contains("32 KB", _notes.Specs[1].Description);

        var list = _notes.Specs[3].InputSchema;
        Assert.Empty(list["properties"]!.AsObject());
        Assert.False(list.ContainsKey("required"));
        Assert.False(ApiBrain.StrictSchema(list)["additionalProperties"]!.GetValue<bool>());
    }

    // ---- Wiki pages ----

    private string Disk(string file) => Path.Combine(_dir, file.Replace('/', Path.DirectorySeparatorChar));

    [Theory]
    [InlineData("strategy/wiki/potions.md")]
    [InlineData("strategy/wiki/enemies/jaw-worm.md")]
    [InlineData("strategy/wiki/encounters/act1-overgrowth.md")]
    [InlineData("strategy/wiki/lessons/new-folder-page.md")]
    [InlineData("strategy/wiki/2nd/a1-b2-c3.md")]
    [InlineData("strategy/wiki/lessons/con-artist.md")]
    [InlineData("strategy/wiki/com10.md")]
    public void Wiki_pages_can_be_written_appended_and_read(string file)
    {
        Assert.False(Write(file, "# Old name\nAn older summary.\n\n## Lessons\n").IsError);
        // A new first line is a new run only for the journal: pages are never archived.
        var written = Write(file, "# Page\nWhat it covers.\n\n## Lessons\n");
        Assert.False(written.IsError, written.Payload.ToJsonString());
        Assert.Null(written.Payload["archived_previous_as"]);
        Assert.False(Call("notes_append", new JsonObject { ["file"] = file, ["content"] = "- (Ironclad, 2026-10-10) A lesson." }).IsError);

        const string expected = "# Page\nWhat it covers.\n\n## Lessons\n- (Ironclad, 2026-10-10) A lesson.\n";
        var read = Call("notes_read", new JsonObject { ["file"] = file });
        Assert.True(read.Payload["exists"]!.GetValue<bool>());
        Assert.Equal(expected, read.Payload["content"]!.GetValue<string>());
        Assert.Equal(expected, File.ReadAllText(Disk(file)));
        Assert.Equal(expected, _notes.ReadText(file));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Disk(file))!));
    }

    [Fact]
    public void A_missing_wiki_page_reads_empty_and_an_append_creates_it_with_its_folder()
    {
        var read = Call("notes_read", new JsonObject { ["file"] = "strategy/wiki/lessons/shops.md" });
        Assert.False(read.IsError);
        Assert.False(read.Payload["exists"]!.GetValue<bool>());

        Assert.False(Call("notes_append", new JsonObject { ["file"] = "strategy/wiki/lessons/shops.md", ["content"] = "# Shops" }).IsError);
        Assert.Equal("# Shops\n", File.ReadAllText(Disk("strategy/wiki/lessons/shops.md")));
    }

    [Theory]
    [InlineData("strategy/wiki/../learned.md")]
    [InlineData("strategy/wiki/enemies/../../learned.md")]
    [InlineData("strategy/wiki/./potions.md")]
    [InlineData("./strategy/wiki/potions.md")]
    [InlineData("/strategy/wiki/potions.md")]
    [InlineData("/etc/wiki/potions.md")]
    [InlineData("C:\\strategy\\wiki\\potions.md")]
    [InlineData("strategy\\wiki\\potions.md")]
    [InlineData("strategy/wiki/enemies\\jaw-worm.md")]
    [InlineData("strategy/wiki/README.md")]
    [InlineData("strategy/wiki/readme.md")]
    [InlineData("strategy/wiki/enemies/readme.md")]
    [InlineData("strategy/wiki/readme/jaw-worm.md")]
    [InlineData("strategy/wiki/nul.md")]
    [InlineData("strategy/wiki/lessons/con.md")]
    [InlineData("strategy/wiki/aux.md")]
    [InlineData("strategy/wiki/prn.md")]
    [InlineData("strategy/wiki/com1.md")]
    [InlineData("strategy/wiki/enemies/lpt9.md")]
    [InlineData("strategy/wiki/nul/jaw-worm.md")]
    [InlineData("strategy/wiki/com3/x.md")]
    [InlineData("strategy/wiki/Potions.md")]
    [InlineData("strategy/wiki/Enemies/jaw-worm.md")]
    [InlineData("strategy/wiki/potions.MD")]
    [InlineData("Strategy/wiki/potions.md")]
    [InlineData("strategy/wiki/enemies/act1/jaw-worm.md")]
    [InlineData("strategy/wiki/potions")]
    [InlineData("strategy/wiki/potions.txt")]
    [InlineData("strategy/wiki/potions.md.md")]
    [InlineData("strategy/wiki/.md")]
    [InlineData("strategy/wiki/enemies/.md")]
    [InlineData("strategy/wiki//jaw-worm.md")]
    [InlineData("strategy/wiki/")]
    [InlineData("strategy/wiki/jaw--worm.md")]
    [InlineData("strategy/wiki/-jaw-worm.md")]
    [InlineData("strategy/wiki/jaw-worm-.md")]
    [InlineData("strategy/wiki/enemies-/jaw-worm.md")]
    [InlineData("strategy/wiki/jaw_worm.md")]
    [InlineData("strategy/wiki/jaw worm.md")]
    [InlineData("strategy/wiki/caf\u00e9.md")]
    [InlineData("strategy/wiki/potions.md ")]
    [InlineData("strategy/wiki/potions.md\n")]
    [InlineData("strategy/wiki.md")]
    [InlineData("strategy/wikis/potions.md")]
    [InlineData("runs/wiki/potions.md")]
    public void Other_wiki_paths_are_refused(string file)
    {
        var outcomes = new[]
        {
            Write(file, "x"),
            Call("notes_append", new JsonObject { ["file"] = file, ["content"] = "x" }),
            Call("notes_read", new JsonObject { ["file"] = file }),
        };
        foreach (var outcome in outcomes)
        {
            Assert.True(outcome.IsError);
            var error = outcome.Payload["error"]!.GetValue<string>();
            Assert.Contains("strategy/wiki/<folder>/<slug>.md", error);
            Assert.Contains("readme and Windows device names", error);
        }
        Assert.Throws<ArgumentException>(() => _notes.ReadText(file));
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void An_absolute_path_to_a_page_is_refused()
    {
        var file = Disk("strategy/wiki/potions.md");
        Assert.True(Write(file, "x").IsError);
        Assert.True(Call("notes_read", new JsonObject { ["file"] = file }).IsError);
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void Wiki_pages_have_the_same_size_limit()
    {
        const string page = "strategy/wiki/cards/ironclad.md";
        Assert.False(Write(page, new string('a', NotesTools.MaxBytes)).IsError);
        Assert.True(Write(page, new string('b', NotesTools.MaxBytes + 1)).IsError);
        Assert.True(Call("notes_append", new JsonObject { ["file"] = page, ["content"] = "x" }).IsError);
        Assert.Equal(new string('a', NotesTools.MaxBytes), File.ReadAllText(Disk(page)));
    }

    private const string FactsPage =
        "# Jaw Worm\nAn Act 1 brute.\n\n## Facts (game files, v0.107.1)\n- 40-44 HP.\n- Chomp: 11 damage.\n\n## Lessons\n- (Ironclad, 2026-10-09) Old lesson.\n";

    [Fact]
    public void A_rewrite_of_a_page_with_facts_may_change_only_its_lessons()
    {
        const string page = "strategy/wiki/enemies/jaw-worm.md";
        Assert.False(Write(page, FactsPage).IsError);

        // Same facts, with the line endings and trailing spaces a model might change; new lessons.
        var kept = "# Jaw Worm  \r\nAn Act 1 brute.\r\n\r\n## Facts (game files, v0.107.1)\r\n- 40-44 HP. \r\n- Chomp: 11 damage.\r\n\r\n\r\n" +
                   "## Lessons\r\n- (Ironclad, 2026-10-10) Corrected lesson.\r\n";
        Assert.False(Write(page, kept).IsError);
        Assert.Equal(kept, File.ReadAllText(Disk(page)));
        Assert.False(Write(page, FactsPage).IsError);

        foreach (var changed in new[]
                 {
                     FactsPage.Replace("Chomp: 11", "Chomp: 12", StringComparison.Ordinal),
                     FactsPage.Replace("- 40-44 HP.\n", "", StringComparison.Ordinal),
                     FactsPage.Replace("An Act 1 brute.", "A brute.", StringComparison.Ordinal),
                     "Prepended.\n" + FactsPage,
                     FactsPage.Replace("## Lessons\n", "", StringComparison.Ordinal),
                     "# Jaw Worm\nAn Act 1 brute.\n\n## Lessons\n- (Ironclad, 2026-10-10) Facts gone.\n",
                     "",
                 })
        {
            var outcome = Write(page, changed);
            Assert.True(outcome.IsError, changed);
            Assert.Contains("Facts section", outcome.Payload["error"]!.GetValue<string>());
        }
        Assert.Equal(FactsPage, File.ReadAllText(Disk(page)));

        // Appending stays open: it can't touch the facts.
        Assert.False(Call("notes_append", new JsonObject { ["file"] = page, ["content"] = "- (Ironclad, 2026-10-10) New." }).IsError);
    }

    [Fact]
    public void A_page_without_facts_can_be_rewritten_freely()
    {
        const string page = "strategy/wiki/lessons/shops.md";
        Assert.False(Write(page, "# Shops\nWhat shops cost.\n\n## Lessons\n- (Ironclad, 2026-10-10) x\n").IsError);
        Assert.False(Write(page, "# Shop prices\nWhat each shop item cost in Act 1.\n\n## Lessons\n").IsError);

        // The journal and learned.md aren't pages, so a Facts heading there means nothing.
        Assert.False(Write("strategy/learned.md", "## Facts\n- a\n").IsError);
        Assert.False(Write("strategy/learned.md", "## Facts\n- b\n").IsError);
    }

    [Theory]
    [InlineData("# A\n## Facts\n- 1\n## Lessons\n- x\n", "# A\n## Facts\n- 1\n## Lessons\n- y\n", true)]
    [InlineData("# A\n## Facts\n- 1\n", "# A\n## Facts\n- 1\n\n## Lessons\n- y\n", true)]
    [InlineData("# A\n## Facts\n- 1\n", "# A\n## Facts\n- 2\n", false)]
    [InlineData("# A\n## facts\n- 1\n## lessons\n", "# A\n## facts\n- 2\n## lessons\n", false)]
    [InlineData("# A\n## Lessons\n- x\n", "# B\n", true)]
    [InlineData("# A\nNo sections.\n", "Anything.", true)]
    public void Keeps_facts_compares_the_part_above_the_lessons(string oldPage, string newPage, bool kept) =>
        Assert.Equal(kept, NotesTools.KeepsFacts(oldPage, newPage));

    [Fact]
    public void Notes_list_shows_the_pages_with_title_and_summary_and_the_fixed_files()
    {
        Write("strategy/learned.md", "# Lessons\n");
        Write("strategy/wiki/potions.md", "# Potions\nEvery potion, with exact numbers.\n\n## Facts (game files, v0.107.1)\n- x\n\n## Lessons\n");
        Write("strategy/wiki/enemies/jaw-worm.md", "\n# Jaw Worm \n\n  An Act 1 brute.  \n## Lessons\n");
        Write("strategy/wiki/lessons/no-summary.md", "# No summary\n\n## Lessons\n- (Ironclad, 2026-10-10) x\n");
        Write("strategy/wiki/lessons/no-heading.md", "Just a line.\nAnother.\n");
        Write("strategy/wiki/lessons/empty.md", "");
        Write("strategy/wiki/run/long.md", "# Long\n" + new string('w', 300) + "\n");
        foreach (var other in new[]
                 {
                     "strategy/wiki/README.md", "strategy/wiki/Notes.md", "strategy/wiki/draft.txt", "strategy/wiki/.potions.md.0123.tmp",
                     "strategy/wiki/enemies/act1/deep.md", "strategy/wiki/Drafts/caps.md", "strategy/wiki/enemies/jaw_worm.md", "strategy/general.md",
                 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Disk(other))!);
            File.WriteAllText(Disk(other), "# Not a page\nHidden.\n");
        }

        var outcome = Call("notes_list", new JsonObject());

        Assert.False(outcome.IsError);
        var wiki = outcome.Payload["wiki"]!.AsArray().Select(p => p!.AsObject()).ToArray();
        Assert.Equal(
        [
            "strategy/wiki/enemies/jaw-worm.md", "strategy/wiki/lessons/empty.md", "strategy/wiki/lessons/no-heading.md",
            "strategy/wiki/lessons/no-summary.md", "strategy/wiki/potions.md", "strategy/wiki/run/long.md",
        ], wiki.Select(p => p["file"]!.GetValue<string>()));
        string? Field(int i, string key) => wiki[i][key]?.GetValue<string>();

        Assert.Equal("Jaw Worm", Field(0, "title"));
        Assert.Equal("An Act 1 brute.", Field(0, "summary"));
        Assert.Equal(new FileInfo(Disk("strategy/wiki/enemies/jaw-worm.md")).Length, wiki[0]["bytes"]!.GetValue<long>());
        Assert.Equal(0, wiki[1]["bytes"]!.GetValue<long>());
        Assert.False(wiki[1].ContainsKey("title"));
        Assert.False(wiki[1].ContainsKey("summary"));
        Assert.Null(Field(2, "title"));
        Assert.Equal("Just a line.", Field(2, "summary"));
        Assert.Equal("No summary", Field(3, "title"));
        Assert.Null(Field(3, "summary"));
        Assert.Equal("Potions", Field(4, "title"));
        Assert.Equal("Every potion, with exact numbers.", Field(4, "summary"));
        Assert.Equal(200, Field(5, "summary")!.Length);
        Assert.EndsWith("...", Field(5, "summary"));

        var files = outcome.Payload["files"]!.AsArray();
        Assert.Equal("runs/current.md", files[0]!["file"]!.GetValue<string>());
        Assert.False(files[0]!["exists"]!.GetValue<bool>());
        Assert.Equal("strategy/learned.md", files[1]!["file"]!.GetValue<string>());
        Assert.True(files[1]!["exists"]!.GetValue<bool>());
        Assert.Equal(10, files[1]!["bytes"]!.GetValue<long>());
    }

    [Fact]
    public void Notes_list_without_a_wiki_is_empty()
    {
        var outcome = Call("notes_list", new JsonObject());
        Assert.False(outcome.IsError);
        Assert.Empty(outcome.Payload["wiki"]!.AsArray());
        Assert.All(outcome.Payload["files"]!.AsArray(), f => Assert.False(f!["exists"]!.GetValue<bool>()));
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
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
    public bool UseFightLog { get; init; }
    public IToolHost? Tools { get; init; }
    public bool AnnounceThrows { get; init; }
    public bool LogThrows { get; init; }

    /// <summary>What the brain's blocked callback returns (the controller's co-op gate).</summary>
    public volatile string? BlockedReason;
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
            },
            blocked: () => BlockedReason,
            fights: UseFightLog ? new FightLog(Dir, _logged.Enqueue, () => Now) : null);
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

    public FakeApi Then(Func<ClaudeResponse> step)
    {
        lock (_gate) _script.Enqueue(step);
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
