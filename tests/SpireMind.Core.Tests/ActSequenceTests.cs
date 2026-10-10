using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class ActSequenceTests
{
    /// <summary>
    /// A scripted fight: a hand of named cards, enemies that can die, and hooks that run when an
    /// action reaches the game. Ids and labels follow CombatReader: "play:2>e0" labelled "Play
    /// Strike on Jaw Worm", with " (e1)" after an enemy's name while another living enemy shares
    /// it, and hand entries with the card's name, cost and text. A card costs 1 unless it says
    /// otherwise: "Strike/0" is a Strike that costs 0, labelled like any Strike. Played cards
    /// leave the hand at once, so every play is a new settled decision.
    /// </summary>
    private sealed class ScriptedFight : IGameBridge
    {
        public List<string> Hand { get; } = [];

        /// <summary>Cards that left the hand by being played, in order.</summary>
        public List<string> Played { get; } = [];

        /// <summary>Cards picked in a hand selection, out of the hand until confirm puts them back.</summary>
        public List<string> Selected { get; } = [];

        /// <summary>The hand selection may be confirmed with nothing picked (retain up to N).</summary>
        public bool OptionalSelection { get; set; }

        /// <summary>Goes into every card's text, as Strength does into an attack's.</summary>
        public int Strength { get; set; }
        public List<(string Name, bool Alive)> Enemies { get; } = [("Jaw Worm", true)];

        /// <summary>Cards played at an enemy; the rest are played without a target.</summary>
        public HashSet<string> Attacks { get; } = ["Strike", "Bash"];

        /// <summary>Cards the game takes without anything happening.</summary>
        public HashSet<string> Duds { get; } = [];

        /// <summary>Cards the game refuses to play.</summary>
        public HashSet<string> Unaffordable { get; } = [];

        /// <summary>Runs after a card left the hand, with its target's index.</summary>
        public Dictionary<string, Action<int?>> OnPlay { get; } = new();

        /// <summary>Runs as each action reaches the game, before it does anything.</summary>
        public Action<string>? OnExecute { get; set; }

        /// <summary>Every id that reached the game, refused ones included.</summary>
        public List<string> Executed { get; } = [];

        public string Screen { get; set; } = "combat";
        public bool Busy { get; set; }

        /// <summary>end_turn leaves the game busy instead of dealing the next hand.</summary>
        public bool EnemyTurnHolds { get; set; }

        public int Turn { get; private set; } = 1;

        public ScriptedFight(params string[] hand) => Hand.AddRange(hand);

        public void Kill(int enemy) => Enemies[enemy] = (Enemies[enemy].Name, false);

        /// <summary>The enemy turn passes and the next hand is dealt.</summary>
        public void NextTurn()
        {
            Turn++;
            Hand.Clear();
            Hand.AddRange(["Strike", "Defend", "Strike"]);
        }

        public static string Name(string card) => card.Split('/')[0];

        private static string Cost(string card) => card.Split('/') is [_, var cost] ? cost : "1";

        public Task<GameSnapshot> CaptureAsync(CancellationToken ct)
        {
            if (Busy)
                return Task.FromResult(GameSnapshot.Busy(Screen, "enemy turn"));

            var actions = new List<LegalAction>();
            if (Screen == "hand_select")
            {
                for (var i = 0; i < Hand.Count; i++)
                    actions.Add(new LegalAction($"select:{i}", $"Select {Name(Hand[i])}"));
                if (OptionalSelection || Selected.Count > 0)
                    actions.Add(new LegalAction("confirm", $"Confirm selection ({Selected.Count} chosen)"));
            }
            else if (Screen == "combat")
            {
                for (var i = 0; i < Hand.Count; i++)
                {
                    var name = Name(Hand[i]);
                    if (!Attacks.Contains(name))
                    {
                        actions.Add(new LegalAction($"play:{i}", $"Play {name}"));
                        continue;
                    }
                    for (var j = 0; j < Enemies.Count; j++)
                        if (Enemies[j].Alive)
                            actions.Add(new LegalAction($"play:{i}>e{j}", $"Play {name} on {EnemyLabel(j)}"));
                }
                actions.Add(new LegalAction("end_turn", "End turn"));
            }
            else
            {
                actions.Add(new LegalAction("proceed", "Proceed"));
            }

            return Task.FromResult(new GameSnapshot
            {
                Screen = Screen,
                AwaitingDecision = true,
                State = new JsonObject
                {
                    ["turn"] = Turn,
                    ["hand"] = new JsonArray(Hand.Select((c, i) => (JsonNode)new JsonObject
                    {
                        ["i"] = i, ["name"] = Name(c), ["cost"] = Cost(c), ["text"] = $"{Name(c)} with {Strength} Strength.", ["playable"] = true,
                    }).ToArray()),
                    ["enemies"] = new JsonArray(Enemies.Select((e, j) => (JsonNode)new JsonObject
                    {
                        ["i"] = j, ["name"] = e.Name, ["alive"] = e.Alive,
                    }).ToArray()),
                },
                Actions = actions,
            });
        }

        public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
        {
            OnExecute?.Invoke(actionId);
            Executed.Add(actionId);

            if (actionId == "end_turn")
            {
                if (EnemyTurnHolds)
                    Busy = true;
                else
                    NextTurn();
                return Task.FromResult(ActionResult.Success);
            }

            if (actionId.StartsWith("play:"))
            {
                var parts = actionId[5..].Split('>');
                var card = Hand[int.Parse(parts[0])];
                var name = Name(card);
                int? target = parts.Length > 1 ? int.Parse(parts[1][1..]) : null;
                if (Unaffordable.Contains(name))
                    return Task.FromResult(ActionResult.Fail("Not enough energy."));
                if (Duds.Contains(name))
                    return Task.FromResult(ActionResult.Success);
                Hand.RemoveAt(int.Parse(parts[0]));
                Played.Add(card);
                if (OnPlay.TryGetValue(name, out var effect))
                    effect(target);
            }
            else if (actionId.StartsWith("select:"))
            {
                var i = int.Parse(actionId[7..]);
                Selected.Add(Hand[i]);
                Hand.RemoveAt(i);
            }
            else if (actionId == "confirm")
            {
                Hand.AddRange(Selected);
                Selected.Clear();
                Screen = "combat";
            }
            return Task.FromResult(ActionResult.Success);
        }

        public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => Task.FromResult(new JsonObject());

        private string EnemyLabel(int j)
        {
            var name = Enemies[j].Name;
            var twins = Enemies.Count(e => e.Alive && e.Name == name);
            return twins > 1 ? $"{name} (e{j})" : name;
        }
    }

    private static (BotTools Tools, ChatHub Chat) Build(ScriptedFight game)
    {
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner)
        {
            PollInterval = TimeSpan.FromMilliseconds(2),
            SettleTime = TimeSpan.FromMilliseconds(10),
        };
        var chat = new ChatHub { BotName = "Ironclad" };
        return (new BotTools(game, waiter, versioner, chat) { ActSettleTimeout = TimeSpan.FromMilliseconds(250) }, chat);
    }

    private static ToolOutcome Call(BotTools tools, string name, JsonObject args)
    {
        // Through JSON text, as the arguments arrive from MCP or the API.
        var parsed = JsonNode.Parse(args.ToJsonString())!.AsObject();
        return tools.CallAsync(name, parsed, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static long Version(BotTools tools) =>
        Call(tools, "get_state", new JsonObject()).Payload["version"]!.GetValue<long>();

    private static ToolOutcome Sequence(BotTools tools, long version, params string[] ids) =>
        Call(tools, "act_sequence", new JsonObject
        {
            ["version"] = version,
            ["actions"] = new JsonArray(ids.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()),
        });

    private static string[] DoneIds(ToolOutcome outcome) =>
        outcome.Payload["done"]!.AsArray().Select(d => d!["id"]!.GetValue<string>()).ToArray();

    private static string[] DoneLabels(ToolOutcome outcome) =>
        outcome.Payload["done"]!.AsArray().Select(d => d!["label"]!.GetValue<string>()).ToArray();

    private static string[] NotRun(ToolOutcome outcome) =>
        outcome.Payload["not_run"]!.AsArray().Select(d => d!.GetValue<string>()).ToArray();

    private static string Stopped(ToolOutcome outcome) => outcome.Payload["stopped"]!.GetValue<string>();

    [Fact]
    public void Plays_a_whole_turn_while_hand_indices_shift()
    {
        var game = new ScriptedFight("Strike", "Defend", "Bash", "Defend");
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:2>e0", "play:1", "end_turn");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0", "play:1>e0", "play:1", "end_turn"], game.Executed);
        Assert.Equal(["play:0>e0", "play:1>e0", "play:1", "end_turn"], DoneIds(outcome));
        Assert.Equal(["Play Strike on Jaw Worm", "Play Bash on Jaw Worm", "Play Defend", "End turn"], DoneLabels(outcome));
        Assert.Equal(["done", "next"], outcome.Payload.AsObject().Select(p => p.Key));
        Assert.Equal(2, outcome.Payload["next"]!["state"]!["turn"]!.GetValue<int>());
        Assert.True(outcome.Payload["next"]!["awaiting_decision"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("play:0>e0", "play:2>e0")]
    [InlineData("play:0>e0", "play:0>e0")]
    public void Copies_of_a_card_are_interchangeable(string first, string second)
    {
        var game = new ScriptedFight("Strike", "Defend", "Strike");
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), first, second);

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0", "play:1>e0"], game.Executed);
        Assert.Equal(["Play Strike on Jaw Worm", "Play Strike on Jaw Worm"], DoneLabels(outcome));
        Assert.Equal(["Defend"], game.Hand);
    }

    [Fact]
    public void Plays_the_copy_it_was_given_when_copies_differ()
    {
        // The model picked the cheap Strike. Once Defend is gone it sits at 1, and the plain one at 0.
        var game = new ScriptedFight("Defend", "Strike", "Strike/0");
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:2>e0", "play:1>e0");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0", "play:1>e0", "play:0>e0"], game.Executed);
        Assert.Equal(["Defend", "Strike/0", "Strike"], game.Played);
    }

    [Fact]
    public void A_copy_that_shifts_into_the_planned_slot_is_not_played_for_the_one_that_left_it()
    {
        var game = new ScriptedFight("Strike", "Strike/0", "Strike");
        var (tools, _) = Build(game);

        // play:1>e0 is still offered after the first play, but it is now the plain Strike.
        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1>e0");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0", "play:0>e0"], game.Executed);
        Assert.Equal(["Strike", "Strike/0"], game.Played);
    }

    [Fact]
    public void Copies_are_told_apart_by_cost_when_every_text_changed()
    {
        var game = new ScriptedFight("Inflame", "Strike", "Strike/0");
        game.OnPlay["Inflame"] = _ => game.Strength += 2;
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:2>e0");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0", "play:1>e0"], game.Executed);
        Assert.Equal(["Inflame", "Strike/0"], game.Played);
    }

    [Fact]
    public void Confirm_is_found_again_though_its_count_moved_on()
    {
        var game = new ScriptedFight("Strike", "Defend", "Bash") { Screen = "hand_select", OptionalSelection = true };
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "select:0", "select:1", "confirm");

        Assert.False(outcome.IsError);
        Assert.Equal(["select:0", "select:0", "confirm"], game.Executed);
        Assert.Equal(["Select Strike", "Select Defend", "Confirm selection (2 chosen)"], DoneLabels(outcome));
        Assert.Equal(["Bash", "Strike", "Defend"], game.Hand);
        Assert.Equal("combat", outcome.Payload["next"]!["screen"]!.GetValue<string>());
    }

    [Fact]
    public void Stops_when_a_card_ends_the_turn_before_a_trailing_end_turn()
    {
        var game = new ScriptedFight("Void Form", "Defend");
        game.OnPlay["Void Form"] = _ => game.NextTurn();
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "end_turn");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal("Your turn ended after 'Play Void Form'.", Stopped(outcome));
        Assert.Equal(["end_turn"], NotRun(outcome));
        Assert.Equal(2, outcome.Payload["next"]!["state"]!["turn"]!.GetValue<int>());
        Assert.Equal(2, game.Turn);
    }

    [Fact]
    public void Stops_when_a_card_ends_the_turn_mid_plan()
    {
        // The next hand has a Strike too, but it was never planned for.
        var game = new ScriptedFight("Void Form", "Strike");
        game.OnPlay["Void Form"] = _ => game.NextTurn();
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal("Your turn ended after 'Play Void Form'.", Stopped(outcome));
        Assert.Equal(["play:1>e0"], NotRun(outcome));
        Assert.Equal(["Strike", "Defend", "Strike"], game.Hand);
    }

    [Fact]
    public void A_twin_keeps_its_target_when_its_tag_goes()
    {
        var game = new ScriptedFight("Bash", "Strike");
        game.Enemies.Clear();
        game.Enemies.AddRange([("Louse", true), ("Louse", true)]);
        game.OnPlay["Bash"] = target => game.Kill(target!.Value);
        var (tools, _) = Build(game);

        // Once e0 dies, e1 is the only Louse and its label loses the " (e1)".
        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1>e1");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0", "play:0>e1"], DoneIds(outcome));
        Assert.Equal(["Play Bash on Louse (e0)", "Play Strike on Louse"], DoneLabels(outcome));
    }

    [Fact]
    public void A_dead_target_ends_the_sequence_instead_of_switching_enemies()
    {
        var game = new ScriptedFight("Bash", "Strike", "Defend");
        game.Enemies.Clear();
        game.Enemies.AddRange([("Louse", true), ("Louse", true)]);
        game.OnPlay["Bash"] = target => game.Kill(target!.Value);
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e1", "play:1>e1", "play:2");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e1"], game.Executed);
        Assert.Equal("'Play Strike on Louse (e1)' is no longer offered.", Stopped(outcome));
        Assert.Equal(["play:1>e1", "play:2"], NotRun(outcome));
        Assert.Contains(outcome.Payload["next"]!["actions"]!.AsArray(), a => a!["id"]!.GetValue<string>() == "play:0>e0");
    }

    [Fact]
    public void Stops_when_the_screen_changes()
    {
        var game = new ScriptedFight("True Grit", "Strike", "Defend");
        game.OnPlay["True Grit"] = _ => game.Screen = "hand_select";
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0", "end_turn");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal(["Play True Grit"], DoneLabels(outcome));
        Assert.Equal("The screen changed to hand_select.", Stopped(outcome));
        Assert.Equal(["play:1>e0", "end_turn"], NotRun(outcome));
        Assert.Equal("hand_select", outcome.Payload["next"]!["screen"]!.GetValue<string>());
        Assert.Equal(["done", "stopped", "not_run", "next"], outcome.Payload.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void Stops_for_a_chat_message_that_arrives_mid_sequence()
    {
        var game = new ScriptedFight("Strike", "Defend", "Defend");
        ChatHub? hub = null;
        game.OnExecute = _ =>
        {
            // A line for another bot isn't for the model; the next one is.
            if (game.Executed.Count == 0) hub!.PostFromHuman("Mark", "Silent, nice one");
            if (game.Executed.Count == 1) hub!.PostFromHuman("Mark", "Ironclad, hold on");
        };
        var (tools, chat) = Build(game);
        hub = chat;

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2", "end_turn");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0", "play:1"], game.Executed);
        Assert.Equal("A chat message arrived.", Stopped(outcome));
        Assert.Equal(["play:2", "end_turn"], NotRun(outcome));
        var message = Assert.Single(outcome.Payload["chat"]!["messages"]!.AsArray())!;
        Assert.Equal("Ironclad, hold on", message["text"]!.GetValue<string>());
        Assert.False(chat.HasUnread);
    }

    [Fact]
    public void Stops_when_paused_mid_sequence()
    {
        var game = new ScriptedFight("Strike", "Defend");
        ChatHub? hub = null;
        game.OnExecute = _ => hub!.PostFromHuman("Mark", "/pause");
        var (tools, chat) = Build(game);
        hub = chat;

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "end_turn");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0"], game.Executed);
        Assert.Equal("Paused by Mark. Call wait_for_decision; you can still talk with say.", Stopped(outcome));
        Assert.Equal(["play:1", "end_turn"], NotRun(outcome));
        var next = outcome.Payload["next"]!;
        Assert.False(next["awaiting_decision"]!.GetValue<bool>());
        Assert.Empty(next["actions"]!.AsArray());
        Assert.True(outcome.Payload["chat"]!["paused"]!.GetValue<bool>());
        Assert.Null(chat.ThinkingSince);
    }

    [Fact]
    public void Stops_when_an_action_changes_nothing()
    {
        var game = new ScriptedFight("Clumsy", "Strike");
        game.Duds.Add("Clumsy");
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0"], DoneIds(outcome));
        Assert.StartsWith("Nothing changed after 'Play Clumsy'.", Stopped(outcome));
        Assert.Equal(["play:1>e0"], NotRun(outcome));
    }

    [Fact]
    public void Stops_when_the_game_does_not_settle()
    {
        var game = new ScriptedFight("Havoc", "Strike");
        game.OnPlay["Havoc"] = _ => game.Busy = true;
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal("The game is still busy (enemy turn). Call wait_for_decision before acting again.", Stopped(outcome));
        Assert.False(outcome.Payload["next"]!["awaiting_decision"]!.GetValue<bool>());
    }

    [Fact]
    public void A_last_step_that_leaves_the_game_busy_warns_like_act()
    {
        var game = new ScriptedFight("Strike") { EnemyTurnHolds = true };
        var (tools, chat) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "end_turn");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0", "end_turn"], DoneIds(outcome));
        Assert.False(outcome.Payload.AsObject().ContainsKey("stopped"));
        Assert.False(outcome.Payload.AsObject().ContainsKey("not_run"));
        Assert.Equal("The game is still busy. Call wait_for_decision before acting again.", outcome.Payload["warning"]!.GetValue<string>());
        Assert.Null(chat.ThinkingSince);
    }

    [Fact]
    public void A_refused_step_stops_and_stays_with_the_ids_not_run()
    {
        var game = new ScriptedFight("Defend", "Strike");
        game.Unaffordable.Add("Strike");
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0", "end_turn");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0"], DoneIds(outcome));
        Assert.Equal("The game refused 'Play Strike on Jaw Worm': Not enough energy.", Stopped(outcome));
        Assert.Equal(["play:1>e0", "end_turn"], NotRun(outcome));

        // Nothing ran at all: that is an error, in the same shape.
        var first = Sequence(tools, Version(tools), "play:0>e0", "end_turn");
        Assert.True(first.IsError);
        Assert.Empty(first.Payload["done"]!.AsArray());
        Assert.Equal(["play:0>e0", "end_turn"], NotRun(first));
        Assert.True(first.Payload["next"]!["awaiting_decision"]!.GetValue<bool>());
    }

    [Fact]
    public void An_unknown_id_is_refused_before_anything_runs()
    {
        var game = new ScriptedFight("Strike", "Defend");
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:9>e0", "end_turn");

        Assert.True(outcome.IsError);
        Assert.Empty(game.Executed);
        Assert.Equal("'play:9>e0' is not a legal action now, so nothing was played.", outcome.Payload["error"]!.GetValue<string>());
        Assert.Equal(3, outcome.Payload["current"]!["actions"]!.AsArray().Count);
    }

    [Fact]
    public void Refuses_like_act_on_a_stale_version_a_pause_or_a_busy_game()
    {
        var game = new ScriptedFight("Strike", "Defend");
        var (tools, chat) = Build(game);
        var version = Version(tools);

        var stale = Sequence(tools, version + 5, "play:0>e0");
        Assert.True(stale.IsError);
        Assert.Equal($"Snapshot changed (you sent version {version + 5}, current is {version}). Pick from the current actions.",
            stale.Payload["error"]!.GetValue<string>());
        Assert.Equal(version, stale.Payload["current"]!["version"]!.GetValue<long>());

        chat.PostFromHuman("Mark", "/pause");
        var paused = Sequence(tools, version, "play:0>e0");
        Assert.True(paused.IsError);
        Assert.StartsWith("Paused by Mark.", paused.Payload["error"]!.GetValue<string>());
        chat.PostFromHuman("Mark", "/resume");

        game.Busy = true;
        var busy = Sequence(tools, Version(tools), "play:0>e0");
        Assert.True(busy.IsError);
        Assert.Equal("Not your decision right now (enemy turn). Call wait_for_decision.", busy.Payload["error"]!.GetValue<string>());

        Assert.Empty(game.Executed);
    }

    [Theory]
    [InlineData("""{"version": V, "actions": []}""", "actions takes 1 to 12 ids; you sent 0.")]
    [InlineData("""{"version": V, "actions": ["end_turn","end_turn","end_turn","end_turn","end_turn","end_turn","end_turn","end_turn","end_turn","end_turn","end_turn","end_turn","end_turn"]}""", "actions takes 1 to 12 ids; you sent 13.")]
    [InlineData("""{"version": V, "actions": "end_turn"}""", "actions must be a list of action ids")]
    [InlineData("""{"version": V, "actions": ["end_turn", 3]}""", "actions must be a list of action ids")]
    [InlineData("""{"actions": ["end_turn"]}""", "version is required")]
    [InlineData("""{"version": V, "actions": ["end_turn", "play:0>e0"]}""", "end_turn can only be the last id, since the turn is over once it runs. Nothing was played.")]
    public void Bad_arguments_are_refused_before_anything_runs(string json, string error)
    {
        var game = new ScriptedFight("Strike");
        var (tools, _) = Build(game);
        var args = JsonNode.Parse(json.Replace("V", Version(tools).ToString()))!.AsObject();

        var outcome = Call(tools, "act_sequence", args);

        Assert.True(outcome.IsError);
        Assert.Equal(error, outcome.Payload["error"]!.GetValue<string>());
        Assert.Empty(game.Executed);
    }

    [Fact]
    public void Takes_twelve_ids()
    {
        var game = new ScriptedFight(Enumerable.Repeat("Defend", 11).ToArray());
        var (tools, _) = Build(game);

        var outcome = Sequence(tools, Version(tools), [.. Enumerable.Repeat("play:0", 11), "end_turn"]);

        Assert.False(outcome.IsError);
        Assert.Equal(12, DoneIds(outcome).Length);
        Assert.Equal(2, outcome.Payload["next"]!["state"]!["turn"]!.GetValue<int>());
    }

    [Fact]
    public void Clears_thinking_while_it_plays_and_marks_the_next_decision()
    {
        var game = new ScriptedFight("Strike", "Defend", "Strike");
        ChatHub? hub = null;
        var duringPlay = new List<DateTime?>();
        game.OnExecute = _ => duringPlay.Add(hub!.ThinkingSince);
        var (tools, chat) = Build(game);
        hub = chat;

        var version = Version(tools);
        var first = chat.ThinkingSince;
        Assert.NotNull(first);

        var outcome = Sequence(tools, version, "play:0>e0", "play:1");
        Assert.False(outcome.IsError);
        Assert.Equal([null, null], duringPlay);
        Assert.True(chat.ThinkingSince > first, "the next decision starts a new clock");

        // A refusal hands back the current decision, so the bot is deciding again.
        chat.MarkThinking(false);
        Assert.True(Sequence(tools, version, "play:0>e0").IsError);
        Assert.NotNull(chat.ThinkingSince);
    }

    [Fact]
    public void Act_still_does_one_action_with_its_old_reply()
    {
        var game = new ScriptedFight("Strike", "Clumsy", "Defend");
        game.Duds.Add("Clumsy");
        game.Unaffordable.Add("Defend");
        var (tools, _) = Build(game);

        var acted = Call(tools, "act", new JsonObject { ["action_id"] = "play:0>e0", ["version"] = Version(tools) });
        Assert.False(acted.IsError);
        Assert.Equal(["done", "next"], acted.Payload.AsObject().Select(p => p.Key));
        Assert.Equal("play:0>e0", acted.Payload["done"]!.GetValue<string>());

        var dud = Call(tools, "act", new JsonObject { ["action_id"] = "play:0" });
        Assert.False(dud.IsError);
        Assert.Equal(["done", "next", "warning"], dud.Payload.AsObject().Select(p => p.Key));
        Assert.StartsWith("Nothing changed after this action.", dud.Payload["warning"]!.GetValue<string>());

        var refused = Call(tools, "act", new JsonObject { ["action_id"] = "play:1" });
        Assert.True(refused.IsError);
        Assert.Equal("Not enough energy.", refused.Payload["error"]!.GetValue<string>());
        Assert.True(refused.Payload["current"]!["awaiting_decision"]!.GetValue<bool>());
        Assert.Equal(["play:0>e0", "play:0", "play:1"], game.Executed);
    }

    [Fact]
    public void Schema_survives_strict_tool_use()
    {
        var (tools, _) = Build(new ScriptedFight("Strike"));
        var spec = tools.Specs.Single(s => s.Name == "act_sequence");
        Assert.Equal(12, spec.InputSchema["properties"]!["actions"]!["maxItems"]!.GetValue<int>());

        var tool = ApiBrain.ToApiTools([spec]).Single()!;
        var schema = tool["input_schema"]!.AsObject();
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(["version", "actions"], schema["required"]!.AsArray().Select(r => r!.GetValue<string>()));

        var actions = schema["properties"]!["actions"]!.AsObject();
        Assert.Equal("array", actions["type"]!.GetValue<string>());
        Assert.Equal("""{"type":"string"}""", actions["items"]!.ToJsonString());
        Assert.Equal(1, actions["minItems"]!.GetValue<int>());
        Assert.False(actions.ContainsKey("maxItems"));
        Assert.EndsWith("(maxItems 12)", actions["description"]!.GetValue<string>());
    }
}
