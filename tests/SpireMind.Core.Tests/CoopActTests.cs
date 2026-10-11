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

    /// <summary>A card; <paramref name="Picks"/> cards are then discarded from the hand, closing at
    /// the last pick for one and with a confirm for more.</summary>
    private sealed record Card(string Name, string Type, int Cost, string Text, bool Aimed, int Picks = 0);

    /// <summary>
    /// A fight shaped like CombatReader's snapshots: "me" with HP, Block and powers, energy,
    /// living enemies by index with HP, Block and powers, incoming_damage, the hand, and a "team"
    /// list with the human (left out when <see cref="Solo"/>). Strike deals 6 at its target,
    /// Defend gives 5 Block, Inflame gives 2 Strength, Iron Wave 5 Block and 5 damage, Survivor
    /// 8 Block and a discard, Tidy Up two discards and a confirm, and the Block Potion 12 Block.
    /// <see cref="OnExecute"/> stands in for the teammate, who plays while the bot's action
    /// resolves, and <see cref="BetweenSteps"/> for one who plays after it settled. Screen "map"
    /// offers two map votes.
    /// </summary>
    private sealed class CoopFight : IGameBridge
    {
        public static readonly Card Strike = new("Strike", "Attack", 1, "Deal 6 damage.", Aimed: true);
        public static readonly Card Defend = new("Defend", "Skill", 1, "Gain 5 Block.", Aimed: false);
        public static readonly Card Inflame = new("Inflame", "Power", 1, "Gain 2 Strength.", Aimed: false);
        public static readonly Card IronWave = new("Iron Wave", "Attack", 1, "Gain 5 Block. Deal 5 damage.", Aimed: true);
        public static readonly Card Survivor = new("Survivor", "Skill", 1, "Gain 8 Block. Discard 1 card.", Aimed: false, Picks: 1);
        public static readonly Card TidyUp = new("Tidy Up", "Skill", 1, "Discard 2 cards.", Aimed: false, Picks: 2);

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
        public List<string> Potions { get; } = [];

        /// <summary>state.team_since in a fight, left out while empty or <see cref="Solo"/>. The
        /// game side clears lines on the bot's own action; this fake keeps them until a test does.</summary>
        public List<string> TeamSince { get; } = [];

        /// <summary>state.team_changed: a line touched the bot, its pet or an enemy.</summary>
        public bool TeamChanged { get; set; }

        /// <summary>Runs with the last action's id on the second capture after it: with no settle
        /// time the first is the step's own settled snapshot, so this lands after the step settled,
        /// right before the next step looks at the game again.</summary>
        public Action<string>? BetweenSteps { get; set; }

        /// <summary>Captures left that show the game busy, as while a teammate's card resolves.</summary>
        public int BusyCaptures { get; set; }

        private int _picksLeft;
        private bool _confirms;
        private int _capturesSinceAction = -1;

        public CoopFight(params Card[] hand) => Hand.AddRange(hand);

        public Task<GameSnapshot> CaptureAsync(CancellationToken ct)
        {
            if (_capturesSinceAction >= 0 && ++_capturesSinceAction == 2)
                BetweenSteps?.Invoke(Executed[^1]);
            if (BusyCaptures > 0)
            {
                BusyCaptures--;
                return Task.FromResult(GameSnapshot.Busy(Screen, "a teammate's card"));
            }
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
            if (!Solo && TeamSince.Count > 0)
            {
                state["team_since"] = new JsonArray(TeamSince.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
                if (TeamChanged) state["team_changed"] = true;
            }

            if (Screen == "hand_select")
            {
                for (var i = 0; i < Hand.Count && _picksLeft > 0; i++)
                    actions.Add(new LegalAction($"select:{i}", $"Select {Hand[i].Name}"));
                if (_picksLeft == 0)
                    actions.Add(new LegalAction("confirm", "Confirm selection"));
                return Task.FromResult(new GameSnapshot { Screen = Screen, AwaitingDecision = true, State = state, Actions = actions });
            }
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
            for (var p = 0; p < Potions.Count; p++)
                actions.Add(new LegalAction($"potion:{p}", $"Use {Potions[p]}"));
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
            _capturesSinceAction = 0;
            OnExecute?.Invoke(actionId);
            if (actionId.StartsWith("potion:"))
            {
                Potions.RemoveAt(int.Parse(actionId[7..]));
                Block += 12;
                return Task.FromResult(ActionResult.Success);
            }
            if (actionId.StartsWith("select:"))
            {
                Hand.RemoveAt(int.Parse(actionId[7..]));
                if (--_picksLeft == 0 && !_confirms)
                    Screen = "combat";
                return Task.FromResult(ActionResult.Success);
            }
            if (actionId == "confirm")
            {
                Screen = "combat";
                return Task.FromResult(ActionResult.Success);
            }
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
            if (card == IronWave)
            {
                Block += 5;
                Hit(int.Parse(parts[1][1..]), 5);
            }
            if (card == Survivor) Block += 8;
            if (card.Picks > 0)
            {
                Screen = "hand_select";
                _picksLeft = card.Picks;
                _confirms = card.Picks > 1;
            }
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

    /// <param name="settle">The waiter's settle time; zero makes a step settle on its first
    /// capture, which <see cref="CoopFight.BetweenSteps"/> counts on.</param>
    private static BotTools Build(CoopFight game, TimeSpan? hold = null, TimeSpan? settle = null, ChatHub? chat = null)
    {
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner)
        {
            PollInterval = TimeSpan.FromMilliseconds(2),
            SettleTime = settle ?? TimeSpan.FromMilliseconds(10),
        };
        return new BotTools(game, waiter, versioner, chat ?? new ChatHub { BotName = "Ironclad" })
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
    public void A_hand_pick_spends_nothing_so_the_sequence_runs_through_it()
    {
        // Survivor's discard pick: energy stays at 2 while picking, and that is no teammate's gift.
        var game = new CoopFight(CoopFight.Survivor, CoopFight.Defend, CoopFight.Strike);
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "select:1", "play:2>e0");

        Assert.False(outcome.IsError);
        Assert.Null(Stopped(outcome));
        Assert.Equal(["play:0", "select:0", "play:0>e0"], game.Executed);
        Assert.Equal(1, game.Energy);
    }

    [Fact]
    public void Two_picks_and_a_confirm_run_through_too()
    {
        var game = new CoopFight(CoopFight.TidyUp, CoopFight.Defend, CoopFight.Inflame, CoopFight.Strike);
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "select:1", "select:2", "confirm", "play:3>e0");

        Assert.Null(Stopped(outcome));
        Assert.Equal(["play:0", "select:0", "select:0", "confirm", "play:0>e0"], game.Executed);
    }

    [Fact]
    public void Stops_when_a_teammate_gives_Block_while_the_bot_picks()
    {
        var game = new CoopFight(CoopFight.Survivor, CoopFight.Defend, CoopFight.Strike);
        game.OnExecute = id => { if (id.StartsWith("select:")) game.Block += 11; };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "select:1", "play:2>e0");

        Assert.Equal(["play:0", "select:0"], game.Executed);
        Assert.Equal("A teammate acted: your Block 8 -> 19. Plan the rest of your turn from next.", Stopped(outcome));
        Assert.Equal(["play:2>e0"], NotRun(outcome));
    }

    [Fact]
    public void Only_the_Block_number_of_a_card_counts_not_its_damage()
    {
        // Iron Wave says 5 Block and 5 damage: its own play is 5 Block, and 4 more is a teammate's.
        var game = new CoopFight(CoopFight.IronWave, CoopFight.Defend, CoopFight.IronWave, CoopFight.Strike);
        var tools = Build(game);
        Assert.Null(Stopped(Sequence(tools, Version(tools), "play:0>e0", "play:1")));

        game.OnExecute = id => { if (id == "play:0>e0") game.Block += 4; };
        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1>e0");

        Assert.Equal("A teammate acted: your Block 10 -> 19. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void A_potion_explains_its_own_step_but_not_a_teammate_after_it()
    {
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.Potions.Add("Block Potion");
        var tools = Build(game, settle: TimeSpan.Zero);
        Assert.Null(Stopped(Sequence(tools, Version(tools), "potion:0", "play:0")));
        Assert.Equal(17, game.Block);

        game.Potions.Add("Block Potion");
        game.BetweenSteps = id => { if (id == "potion:0") game.Block += 7; };
        var outcome = Sequence(tools, Version(tools), "potion:0", "play:0>e0");

        Assert.Equal(["potion:0", "play:0", "potion:0"], game.Executed);
        Assert.Equal("A teammate acted: your Block 29 -> 36. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void Stops_when_a_teammate_gives_Block_after_a_step_settled()
    {
        // The teammate's card resolves after the Defend settled: the game is busy for a moment,
        // then the bot has 7 more Block than its own Defend gave.
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.BetweenSteps = id =>
        {
            if (id != "play:0") return;
            game.BusyCaptures = 3;
            game.Block += 7;
        };
        var tools = Build(game, settle: TimeSpan.Zero);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0");

        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal("A teammate acted: your Block 5 -> 12. Plan the rest of your turn from next.", Stopped(outcome));
        Assert.Equal(12, outcome.Payload["next"]!["state"]!["me"]!["block"]!.GetValue<int>());
    }

    [Fact]
    public void Stops_when_a_teammate_kills_the_planned_target_after_a_step_settled()
    {
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.Enemies.Add(new Enemy("Louse", 10));
        game.BetweenSteps = id => { if (id == "play:0") game.Hit(1, 10); };
        var tools = Build(game, settle: TimeSpan.Zero);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e1");

        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal("A teammate acted: Louse (e1) is gone. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Theory]
    [InlineData("Ironclad, hold on", "A chat message arrived.")]
    [InlineData("/pause", "Paused by Mark. Call wait_for_decision; you can still talk with say.")]
    public void Stops_for_chat_or_a_pause_that_comes_while_a_teammates_card_resolves(string line, string why)
    {
        var chat = new ChatHub { BotName = "Ironclad" };
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike, CoopFight.Strike);
        game.BetweenSteps = id =>
        {
            if (id != "play:0") return;
            game.BusyCaptures = 3;
            chat.PostFromHuman("Mark", line);
        };
        var tools = Build(game, settle: TimeSpan.Zero, chat: chat);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e0", "play:2>e0");

        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal(why, Stopped(outcome));
        Assert.Equal(["play:1>e0", "play:2>e0"], NotRun(outcome));
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

    // ---- act_sequence and the game's record of what the others did (team_since) ----

    private const string Lift = "Mark played Lift on you: +11 Block";
    private const string Bash = "Mark played Bash on Jaw Worm (e0): 8 damage, 2 Vulnerable";

    [Fact]
    public void A_new_line_that_touched_the_bot_stops_with_its_text_rather_than_the_numbers()
    {
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend);
        game.OnExecute = id =>
        {
            if (id != "play:0>e0") return;
            game.Block += 11;
            game.TeamSince.Add(Lift);
            game.TeamChanged = true;
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.Equal(["play:0>e0"], game.Executed);
        Assert.Equal($"A teammate acted: {Lift}. Plan the rest of your turn from next.", Stopped(outcome));
        Assert.Equal(["play:1", "play:2"], NotRun(outcome));
    }

    [Fact]
    public void A_new_line_about_an_enemy_the_plan_leaves_alone_stops_too()
    {
        // The numbers only watch the planned targets; the record's team_changed covers every enemy.
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.Enemies.Add(new Enemy("Louse", 20));
        game.OnExecute = id =>
        {
            if (id != "play:0") return;
            game.Hit(0, 8);
            game.Enemies[0].Powers["Vulnerable"] = 2;
            game.TeamSince.Add(Bash);
            game.TeamChanged = true;
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e1");

        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal($"A teammate acted: {Bash}. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void Lines_the_model_planned_with_do_not_stop_the_sequence()
    {
        // The game side clears them with the first step; this fake keeps them, as an ignored
        // hand-over would, and they are still old news.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend) { Block = 11 };
        game.TeamSince.Add(Lift);
        game.TeamChanged = true;
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.Null(Stopped(outcome));
        Assert.Equal(3, game.Executed.Count);
    }

    [Fact]
    public void A_new_line_without_team_changed_does_not_stop_the_sequence()
    {
        // Mark blocking for himself is not the bot's business.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend);
        game.OnExecute = id =>
        {
            if (id != "play:0>e0") return;
            game.MateBlock = 5;
            game.TeamSince.Add("Mark played Defend: Mark +5 Block");
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1");

        Assert.Null(Stopped(outcome));
        Assert.Equal(["play:0>e0", "play:0"], game.Executed);
    }

    [Fact]
    public void The_left_out_line_is_never_news()
    {
        // Nine lines the model planned with, shown as the newest seven; one more pushes the oldest
        // of those out and raises the count.
        var earlier = Enumerable.Range(1, 9).Select(n => $"Mark played Defend: Mark +{n} Block").ToList();
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend);
        game.TeamSince.AddRange(["(2 earlier lines left out)", .. earlier.Skip(2)]);
        game.OnExecute = id =>
        {
            if (id != "play:0>e0") return;
            game.TeamSince.Clear();
            game.TeamSince.AddRange(["(3 earlier lines left out)", .. earlier.Skip(3), Lift]);
            game.TeamChanged = true;
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.Equal(["play:0>e0"], game.Executed);
        Assert.Equal($"A teammate acted: {Lift}. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void A_new_line_like_one_that_slid_out_of_view_is_still_news()
    {
        // The Defend line the model saw slides out of the window, and Mark plays another Defend
        // that reads the same: the left-out count says the old one is gone from view.
        const string defend = "Mark played Defend: Mark +5 Block";
        var others = Enumerable.Range(1, 6).Select(n => $"Mark played Shrug It Off {n}: Mark +8 Block").ToList();
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend);
        game.TeamSince.AddRange(["(2 earlier lines left out)", defend, .. others]);
        game.OnExecute = id =>
        {
            if (id != "play:0>e0") return;
            game.TeamSince.Clear();
            game.TeamSince.AddRange(["(4 earlier lines left out)", .. others.Skip(1), defend, Lift]);
            game.TeamChanged = true;
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.Equal(["play:0>e0"], game.Executed);
        Assert.Equal($"A teammate acted: {defend}; {Lift}. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void A_new_line_does_not_stop_while_a_line_the_model_planned_with_may_have_set_team_changed()
    {
        // The first step left the Lift in place (the game side kept it), so team_changed may be
        // the Lift's: Mark ending his turn and taking it back are no news for the bot.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend) { Block = 11 };
        game.TeamSince.Add(Lift);
        game.TeamChanged = true;
        game.OnExecute = id =>
        {
            if (game.Executed.Count == 1) game.TeamSince.Add("Mark ended their turn");
            if (game.Executed.Count == 2) game.TeamSince.Add("Mark took back their end of turn");
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.Null(Stopped(outcome));
        Assert.Equal(3, game.Executed.Count);
    }

    [Fact]
    public void A_line_after_the_first_step_cleared_the_planned_ones_stops_though_they_had_team_changed()
    {
        // The first step cleared the Lift, so team_changed after it is about what came since.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend) { Block = 11 };
        game.TeamSince.Add(Lift);
        game.TeamChanged = true;
        game.OnExecute = id =>
        {
            if (id != "play:0>e0") return;
            game.TeamSince.Clear();
            game.TeamSince.Add(Bash);
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.Equal(["play:0>e0"], game.Executed);
        Assert.Equal($"A teammate acted: {Bash}. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void Lines_a_sequence_on_an_older_snapshot_ran_past_are_not_news_later()
    {
        // The current snapshot's Defend line touched nothing, so the sequence runs from the older
        // one, and the first step keeps it (it came less than 700 ms after the board changed).
        const string defend = "Mark played Defend: Mark +5 Block";
        var game = new CoopFight(CoopFight.Defend, CoopFight.Defend);
        var tools = Build(game);
        var version = Version(tools);
        game.MateBlock = 5;
        game.TeamSince.Add(defend);
        game.OnExecute = id =>
        {
            if (game.Executed.Count != 1) return;
            game.TeamSince.Add(Bash);
            game.TeamChanged = true;
        };

        var outcome = Sequence(tools, version, "play:0", "play:1");

        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal($"A teammate acted: {Bash}. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void A_line_judged_after_one_step_is_not_judged_again()
    {
        // After the first step a line comes that touched nothing, so the sequence goes on. After
        // the second, one that did: only that one is news.
        var game = new CoopFight(CoopFight.Defend, CoopFight.Defend, CoopFight.Strike);
        game.OnExecute = id =>
        {
            if (game.Executed.Count == 1)
            {
                game.MateBlock = 5;
                game.TeamSince.Add("Mark played Defend: Mark +5 Block");
            }
            if (game.Executed.Count == 2)
            {
                game.Hit(0, 8);
                game.Enemies[0].Powers["Vulnerable"] = 2;
                game.TeamSince.Add(Bash);
                game.TeamChanged = true;
            }
        };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1", "play:2>e0");

        Assert.Equal(["play:0", "play:0"], game.Executed);
        Assert.Equal($"A teammate acted: {Bash}. Plan the rest of your turn from next.", Stopped(outcome));
        Assert.Equal(["play:2>e0"], NotRun(outcome));
    }

    [Fact]
    public void A_line_that_touched_something_after_a_step_settled_stops_the_sequence()
    {
        // Seen in the capture taken before the next step; no number the plan watches moved.
        const string weak = "Mark played Neutralize on Jaw Worm (e0): 1 Weak";
        var game = new CoopFight(CoopFight.Defend, CoopFight.Strike);
        game.Enemies.Add(new Enemy("Louse", 20));
        game.BetweenSteps = id =>
        {
            if (id != "play:0") return;
            game.TeamSince.Add(weak);
            game.TeamChanged = true;
        };
        var tools = Build(game, settle: TimeSpan.Zero);

        var outcome = Sequence(tools, Version(tools), "play:0", "play:1>e1");

        Assert.Equal(["play:0"], game.Executed);
        Assert.Equal($"A teammate acted: {weak}. Plan the rest of your turn from next.", Stopped(outcome));
    }

    [Fact]
    public void Numbers_with_no_new_line_still_stop_with_the_numbers()
    {
        // The Lift was planned with; 6 more Block came with no line yet.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend, CoopFight.Defend) { Block = 11 };
        game.TeamSince.Add(Lift);
        game.TeamChanged = true;
        game.OnExecute = id => { if (id == "play:0>e0") game.Block += 6; };
        var tools = Build(game);

        var outcome = Sequence(tools, Version(tools), "play:0>e0", "play:1", "play:2");

        Assert.Equal(["play:0>e0"], game.Executed);
        Assert.Equal("A teammate acted: your Block 11 -> 17. Plan the rest of your turn from next.", Stopped(outcome));
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
    public void An_act_on_an_older_snapshot_is_refused_with_a_new_line_that_touched_something()
    {
        // Mark hit an enemy the Defend has nothing to do with: no number the act rests on moved,
        // but the record says a teammate touched the board since the model's snapshot.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend);
        var tools = Build(game);
        var version = Version(tools);
        game.Hit(0, 8);
        game.Enemies[0].Powers["Vulnerable"] = 2;
        game.TeamSince.Add(Bash);
        game.TeamChanged = true;

        var outcome = Act(tools, version, "play:1");
        var sequence = Sequence(tools, version, "play:1", "play:0>e0");

        var why = $"A teammate acted since that snapshot: {Bash}. Nothing was done; plan from the current snapshot.";
        Assert.True(outcome.IsError);
        Assert.Equal(why, outcome.Payload["error"]!.GetValue<string>());
        Assert.Equal(why, sequence.Payload["error"]!.GetValue<string>());
        Assert.Empty(game.Executed);
    }

    [Fact]
    public void An_act_on_an_older_snapshot_runs_past_lines_it_had_or_that_touched_nothing()
    {
        // The line was in the model's snapshot already.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend) { Block = 11 };
        game.TeamSince.Add(Lift);
        game.TeamChanged = true;
        var tools = Build(game);
        var version = Version(tools);
        game.MateBlock = 7;
        Assert.False(Act(tools, version, "play:1").IsError);

        // A new line, but team_changed isn't set: Mark blocked for himself.
        game = new CoopFight(CoopFight.Strike, CoopFight.Defend);
        tools = Build(game);
        version = Version(tools);
        game.MateBlock = 5;
        game.TeamSince.Add("Mark played Defend: Mark +5 Block");
        Assert.False(Act(tools, version, "play:1").IsError);
        Assert.Equal(["play:1"], game.Executed);
    }

    [Fact]
    public void An_act_on_an_older_snapshot_runs_past_a_new_line_while_its_own_lines_set_team_changed()
    {
        // The Lift was in the model's snapshot and nothing cleared it, so team_changed in the
        // current one may be the Lift's alone: Mark ending his turn touched nothing.
        var game = new CoopFight(CoopFight.Strike, CoopFight.Defend) { Block = 11 };
        game.TeamSince.Add(Lift);
        game.TeamChanged = true;
        var tools = Build(game);
        var version = Version(tools);
        game.TeamSince.Add("Mark ended their turn");

        var outcome = Act(tools, version, "play:1");

        Assert.False(outcome.IsError);
        Assert.Equal(["play:1"], game.Executed);
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
