using System.Diagnostics;
using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>
/// act and act_sequence in a fight with a teammate who plays at the same time, and
/// wait_for_decision holding a co-op decision it already handed over.
/// </summary>
public class CoopActTests
{
    private sealed class Enemy(string name, int hp)
    {
        public string Name { get; } = name;
        public int Hp { get; set; } = hp;
        public int Block { get; set; }
        public Dictionary<string, int> Powers { get; } = new();
        public bool Alive => Hp > 0;
    }

    private sealed record Card(string Name, string Type, int Cost, string Text, bool Aimed);

    /// <summary>
    /// A fight shaped like CombatReader's snapshots: "me" with HP, Block and powers, energy,
    /// living enemies by index with HP, Block and powers, incoming_damage, the hand, and a "team"
    /// list with the human (left out when <see cref="Solo"/>). Strike deals 6 at its target,
    /// Defend gives 5 Block, Inflame gives 2 Strength. <see cref="OnExecute"/> stands in for the
    /// teammate, who plays while the bot's action resolves. Screen "map" offers two map votes.
    /// </summary>
    private sealed class CoopFight : IGameBridge
    {
        public static readonly Card Strike = new("Strike", "Attack", 1, "Deal 6 damage.", Aimed: true);
        public static readonly Card Defend = new("Defend", "Skill", 1, "Gain 5 Block.", Aimed: false);
        public static readonly Card Inflame = new("Inflame", "Power", 1, "Gain 2 Strength.", Aimed: false);

        public List<Card> Hand { get; } = [];
        public List<Enemy> Enemies { get; } = [new("Jaw Worm", 40)];
        public int Hp { get; set; } = 50;
        public int Block { get; set; }
        public int Energy { get; set; } = 3;
        public Dictionary<string, int> Powers { get; } = new();
        public int Incoming { get; set; } = 12;
        public int MateBlock { get; set; }
        public string? MateVote { get; set; }
        public bool Solo { get; set; }
        public string Screen { get; set; } = "combat";
        public int Turn { get; private set; } = 1;
        public Action<string>? OnExecute { get; set; }
        public List<string> Executed { get; } = [];

        public CoopFight(params Card[] hand) => Hand.AddRange(hand);

        public Task<GameSnapshot> CaptureAsync(CancellationToken ct)
        {
            var state = new JsonObject();
            var actions = new List<LegalAction>();
            if (!Solo)
            {
                var mate = new JsonObject { ["id"] = "p1", ["name"] = "Mark", ["hp"] = 60, ["human"] = true };
                if (MateBlock > 0) mate["block"] = MateBlock;
                if (Screen == "map" && MateVote is not null) mate["map_vote"] = MateVote;
                state["team"] = new JsonArray(mate);
            }
            if (Screen == "map")
            {
                actions.Add(new LegalAction("map:0", "Travel to the Monster at (0,1)"));
                actions.Add(new LegalAction("map:1", "Travel to the Elite at (1,1)"));
                return Task.FromResult(new GameSnapshot { Screen = "map", AwaitingDecision = true, State = state, Actions = actions });
            }

            state["turn"] = Turn;
            state["round"] = Turn;
            state["energy"] = Energy;
            state["me"] = Creature("Ironclad", Hp, Block, Powers);
            if (!Solo) state["you"] = "p0";
            var enemies = new JsonArray();
            for (var j = 0; j < Enemies.Count; j++)
            {
                if (!Enemies[j].Alive) continue;
                var e = Creature(Enemies[j].Name, Enemies[j].Hp, Enemies[j].Block, Enemies[j].Powers);
                e["i"] = j;
                enemies.Add(e);
            }
            state["enemies"] = enemies;
            state["incoming_damage"] = Incoming;
            state["hand"] = new JsonArray(Hand.Select((c, i) => (JsonNode)new JsonObject
            {
                ["i"] = i, ["name"] = c.Name, ["cost"] = c.Cost.ToString(), ["type"] = c.Type, ["text"] = c.Text, ["playable"] = true,
            }).ToArray());

            for (var i = 0; i < Hand.Count; i++)
            {
                if (!Hand[i].Aimed)
                {
                    actions.Add(new LegalAction($"play:{i}", $"Play {Hand[i].Name}"));
                    continue;
                }
                for (var j = 0; j < Enemies.Count; j++)
                    if (Enemies[j].Alive)
                        actions.Add(new LegalAction($"play:{i}>e{j}", $"Play {Hand[i].Name} on {Enemies[j].Name}"));
            }
            actions.Add(new LegalAction("end_turn", "End turn"));
            return Task.FromResult(new GameSnapshot { Screen = "combat", AwaitingDecision = true, State = state, Actions = actions });
        }

        private static JsonObject Creature(string name, int hp, int block, Dictionary<string, int> powers)
        {
            var json = new JsonObject { ["name"] = name, ["hp"] = hp, ["max_hp"] = Math.Max(hp, 50) };
            if (block > 0) json["block"] = block;
            if (powers.Count > 0)
                json["powers"] = new JsonArray(powers.Select(p => (JsonNode)new JsonObject
                {
                    ["name"] = p.Key, ["amount"] = p.Value, ["kind"] = "Buff", ["text"] = $"{p.Key} {p.Value}.",
                }).ToArray());
            return json;
        }

        public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
        {
            Executed.Add(actionId);
            OnExecute?.Invoke(actionId);
            if (actionId.StartsWith("map:"))
            {
                Screen = "combat";
                return Task.FromResult(ActionResult.Success);
            }
            if (actionId == "end_turn")
            {
                Turn++;
                Hand.Clear();
                Hand.AddRange([Strike, Defend]);
                return Task.FromResult(ActionResult.Success);
            }
            var parts = actionId[5..].Split('>');
            var card = Hand[int.Parse(parts[0])];
            Hand.RemoveAt(int.Parse(parts[0]));
            Energy -= card.Cost;
            if (card == Defend) Block += 5;
            if (card == Inflame) Powers["Strength"] = Powers.GetValueOrDefault("Strength") + 2;
            if (card == Strike) Hit(int.Parse(parts[1][1..]), 6);
            return Task.FromResult(ActionResult.Success);
        }

        public void Hit(int enemy, int damage)
        {
            var e = Enemies[enemy];
            var blocked = Math.Min(e.Block, damage);
            e.Block -= blocked;
            e.Hp = Math.Max(0, e.Hp - (damage - blocked));
        }

        public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => Task.FromResult(new JsonObject());
    }

    private static BotTools Build(CoopFight game, TimeSpan? hold = null)
    {
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner)
        {
            PollInterval = TimeSpan.FromMilliseconds(2),
            SettleTime = TimeSpan.FromMilliseconds(10),
        };
        return new BotTools(game, waiter, versioner, new ChatHub { BotName = "Ironclad" })
        {
            ActSettleTimeout = TimeSpan.FromMilliseconds(250),
            RepeatWaitHold = hold ?? TimeSpan.FromSeconds(60),
        };
    }

    private static ToolOutcome Call(BotTools tools, string name, JsonObject args) =>
        tools.CallAsync(name, JsonNode.Parse(args.ToJsonString())!.AsObject(), CancellationToken.None).GetAwaiter().GetResult();

    private static long Version(BotTools tools) =>
        Call(tools, "get_state", new JsonObject()).Payload["version"]!.GetValue<long>();

    private static ToolOutcome Sequence(BotTools tools, long version, params string[] ids) =>
        Call(tools, "act_sequence", new JsonObject
        {
            ["version"] = version,
            ["actions"] = new JsonArray(ids.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()),
        });

    private static ToolOutcome Act(BotTools tools, long version, string id) =>
        Call(tools, "act", new JsonObject { ["action_id"] = id, ["version"] = version });

    private static string[] NotRun(ToolOutcome outcome) =>
        outcome.Payload["not_run"]!.AsArray().Select(d => d!.GetValue<string>()).ToArray();

    private static string? Stopped(ToolOutcome outcome) => outcome.Payload["stopped"]?.GetValue<string>();

    // ---- act_sequence ----

    [Fact]
    public void Stops_when_a_teammate_gives_Block_during_a_step()
    {
        // Mark's report: he gave the bot Block and it played its planned Defends anyway.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend);
        game.OnExecute = id => { if (id == "play:0>e0") game.Block += 11; };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0"], game.Executed);
        Assert.Equal("A teammate acted: your Block 0 -> 11. Plan the rest of your turn from next.", Stopped(outcome));
        Assert.Equal(["play:1", "play:2"], NotRun(outcome));
        Assert.Equal(11, outcome.Payload["next"]!["state"]!["me"]!["block"]!.GetValue<int>());
    }

    [Fact]
    public void Block_beyond_what_the_own_card_gives_is_a_teammate()
    {
        var game = new CoopFight(CoopFight.Defend, CoopFight.Defend, CoopFight.Strike);
        game.OnExecute = id => { if (game.Executed.Count == 1) game.Block += 8; };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1", "play:2>e0");

        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal("A teammate acted: your Block 0 -> 13. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void Own_cards_are_not_taken_for_a_teammate()
    {
        var game = new CoopFight(CoopFight.Inflame, CoopFight.Defend, CoopFight.Strike);
        game.Enemies[0].Hp = 6;
        game.Enemies.Add(new Enemy("Louse", 20));
        var tools = Build(game);

        // Inflame gives Strength, Defend Block, and the Strike kills the Jaw Worm, all the bot's own doing.
        var outcome = Sequence(tools, Version(tools), "play:0", "play:1", "play:2>e0", "end_turn");

        Assert.False(outcome.IsError);
        Assert.Null(Stopped(outcome));
        Assert.Equal(["play:0", "play:0", "play:0>e0", "end_turn"], game.Executed);
    }

    [Fact]
    public void Own_attack_on_the_planned_target_is_not_a_teammate()
    {
        var game = new CoopFight(CoopFight.Strike, CoopFight.Strike);
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1>e0");

        Assert.Null(Stopped(outcome));
        Assert.Equal(28, game.Enemies[0].Hp);
    }

    [Fact]
    public void Stops_when_a_teammate_kills_an_enemy()
    {
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.Enemies.Add(new Enemy("Louse", 10));
        game.OnExecute = id => { if (id == "play:0") game.Hit(1, 10); };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e1");

        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal("A teammate acted: Louse (e1) is gone. Plan the rest of your turn from next.", Stopped(outcome));
        Assert.Equal(["play:1>e1"], NotRun(outcome));
    }

    [Fact]
    public void Stops_when_a_teammate_hits_the_planned_target()
    {
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.Enemies[0].Block = 4;
        game.OnExecute = id => { if (id == "play:0") game.Hit(0, 9); };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0");

        Assert.Equal("A teammate acted: Jaw Worm (e0) HP 40 -> 35; Jaw Worm (e0) Block 4 -> 0. Plan the rest of your turn from next.",
            Stopped(outcome));
    }

    [Fact]
    public void A_teammate_hitting_an_enemy_the_plan_leaves_alone_does_not_stop_it()
    {
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.Enemies.Add(new Enemy("Louse", 20));
        game.OnExecute = id => { if (id == "play:0") game.Hit(0, 5); };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e1");

        Assert.Null(Stopped(outcome));
        Assert.Equal(["play:0", "play:0>e1"], game.Executed);
    }

    [Fact]
    public void Stops_when_a_teammate_debuffs_the_planned_target()
    {
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.OnExecute = id => { if (id == "play:0") game.Enemies[0].Powers["Vulnerable"] = 2; };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0");

        Assert.Equal("A teammate acted: Jaw Worm (e0) Vulnerable 0 -> 2. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void Stops_when_a_teammate_gives_energy_or_Strength_or_changes_the_incoming_damage()
    {
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike, CoopFight.Strike);
        game.OnExecute = id =>
        {
            if (id != "play:0") return;
            game.Energy += 2;
            game.Powers["Strength"] = 5;
            game.Incoming = 9;
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0", "play:2>e0");

        Assert.Equal("A teammate acted: your energy 3 -> 4; your Strength 0 -> 5; incoming damage 12 -> 9. Plan the rest of your turn from next.",
            Stopped(outcome));
    }

    [Fact]
    public void A_solo_fight_runs_the_whole_plan_as_before()
    {
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend) { Solo = true };
        game.OnExecute = id => { if (id == "play:0>e0") game.Block += 11; };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.Null(Stopped(outcome));
        Assert.Equal(3, game.Executed.Count);
        Assert.Equal(21, game.Block);
    }

    [Fact]
    public void A_sequence_from_an_older_snapshot_runs_when_only_the_teammate_moved()
    {
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend);
        var tools = Build(game);
        var version = Version(tools);
        game.MateBlock = 7; // the human blocks for themselves
        Assert.NotEqual(version, Version(tools));

        var outcome = Sequence(tools, version, "play:0>e0", "play:1");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:0>e0", "play:0"], game.Executed);
    }

    // ---- act ----

    [Fact]
    public void An_act_on_an_older_snapshot_runs_when_only_the_teammate_moved()
    {
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend);
        var tools = Build(game);
        var version = Version(tools);
        game.MateBlock = 7;

        var outcome = Act(tools, version, "play:1");

        Assert.False(outcome.IsError);
        Assert.Equal("play:1", outcome.Payload["done"]!.GetValue<string>());
        Assert.Equal(["play:1"], game.Executed);
    }

    [Fact]
    public void An_act_on_an_older_snapshot_is_refused_with_what_the_teammate_changed()
    {
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend);
        var tools = Build(game);
        var version = Version(tools);
        game.Block = 11;

        var outcome = Act(tools, version, "play:1");

        Assert.True(outcome.IsError);
        Assert.Empty(game.Executed);
        Assert.Equal("A teammate acted since that snapshot: your Block 0 -> 11. Nothing was done; plan from the current snapshot.",
            outcome.Payload["error"]!.GetValue<string>());
        Assert.Equal(11, outcome.Payload["current"]!["state"]!["me"]!["block"]!.GetValue<int>());
    }

    [Fact]
    public void An_act_on_a_snapshot_from_before_the_bots_own_action_is_still_refused()
    {
        // Two acts sent on one snapshot: the second must not run on what the first led to.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend);
        var tools = Build(game);
        var version = Version(tools);

        Assert.False(Act(tools, version, "play:1").IsError);
        var second = Act(tools, version, "play:1");

        Assert.True(second.IsError);
        Assert.StartsWith("Snapshot changed", second.Payload["error"]!.GetValue<string>());
        Assert.Equal(["play:1"], game.Executed);
    }

    [Fact]
    public void A_solo_act_on_an_older_snapshot_is_refused_as_before()
    {
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend) { Solo = true };
        var tools = Build(game);
        var version = Version(tools);
        game.Hp = 49;

        var outcome = Act(tools, version, "play:1");

        Assert.True(outcome.IsError);
        Assert.StartsWith("Snapshot changed", outcome.Payload["error"]!.GetValue<string>());
        Assert.Empty(game.Executed);
    }

    // ---- wait_for_decision ----

    private static (long Version, TimeSpan Took) Wait(Func<JsonObject, Task<ToolOutcome>> call, int seconds = 30)
    {
        var watch = Stopwatch.StartNew();
        var outcome = call(new JsonObject { ["timeout_seconds"] = seconds }).GetAwaiter().GetResult();
        return (outcome.Payload["version"]!.GetValue<long>(), watch.Elapsed);
    }

    [Fact]
    public void A_repeated_wait_holds_the_co_op_decision_it_already_returned()
    {
        var game = new CoopFight { Screen = "map" };
        var tools = Build(game, hold: TimeSpan.FromMilliseconds(400));
        Task<ToolOutcome> Call(JsonObject a) => tools.CallAsync("wait_for_decision", a, CancellationToken.None);

        var first = Wait(Call);
        var second = Wait(Call);

        Assert.True(first.Took < TimeSpan.FromMilliseconds(300), $"first wait took {first.Took}");
        Assert.Equal(first.Version, second.Version);
        Assert.True(second.Took >= TimeSpan.FromMilliseconds(350), $"second wait took {second.Took}");
    }

    [Fact]
    public void A_held_wait_ends_when_the_teammate_votes()
    {
        var game = new CoopFight { Screen = "map" };
        var tools = Build(game, hold: TimeSpan.FromSeconds(20));
        Task<ToolOutcome> Call(JsonObject a) => tools.CallAsync("wait_for_decision", a, CancellationToken.None);

        var first = Wait(Call);
        _ = Task.Delay(150).ContinueWith(_ => game.MateVote = "(1,1)");
        var second = Wait(Call);

        Assert.NotEqual(first.Version, second.Version);
        Assert.True(second.Took >= TimeSpan.FromMilliseconds(100), $"second wait took {second.Took}");
        Assert.True(second.Took < TimeSpan.FromSeconds(10), $"second wait took {second.Took}");
    }

    [Fact]
    public void A_wait_after_an_act_or_in_a_solo_run_or_for_the_brain_returns_at_once()
    {
        var game = new CoopFight { Screen = "map" };
        var tools = Build(game, hold: TimeSpan.FromSeconds(20));
        Task<ToolOutcome> Call(JsonObject a) => tools.CallAsync("wait_for_decision", a, CancellationToken.None);
        var host = new BotToolsHost(tools);
        Task<ToolOutcome> Brain(JsonObject a) => host.CallAsync("wait_for_decision", a, CancellationToken.None);

        // The in-game brain holds repeats itself.
        Wait(Brain);
        Assert.True(Wait(Brain).Took < TimeSpan.FromSeconds(5));

        // An act in between: the model isn't repeating itself.
        var shown = Wait(Call);
        Assert.True(Act(tools, shown.Version, "map:5").IsError); // refused, but still an act
        Assert.True(Wait(Call).Took < TimeSpan.FromSeconds(5));

        game.Solo = true;
        Wait(Call);
        Assert.True(Wait(Call).Took < TimeSpan.FromSeconds(5));
    }
}
