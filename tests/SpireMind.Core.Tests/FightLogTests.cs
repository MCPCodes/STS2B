using System.Text.Json.Nodes;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class FightLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-fights-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _logged = [];
    private readonly FightLog _fights;
    private DateTime _now = new(2026, 10, 11, 14, 0, 0, DateTimeKind.Local);

    public FightLogTests()
    {
        _fights = new FightLog(_dir, _logged.Add, () => _now);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    // ---- Snapshot fixtures, in the shapes docs/screens.md gives ----

    // A deciding combat board: Ironclad, act 1 floor 5, the slimes.
    private static JsonObject Board(int turn, int hp, string potion0 = "null", string enemies = Slimes, int floor = 5, string room = "Monster") =>
        JsonNode.Parse($$$"""
        {
          "version": {{{100 + turn}}}, "screen": "combat", "awaiting_decision": true,
          "state": {
            "run": {"act": 1, "floor": {{{floor}}}, "ascension": 0, "room": "{{{room}}}"},
            "turn": {{{turn}}}, "round": {{{turn}}}, "energy": 3, "max_energy": 3,
            "me": {"name": "The Ironclad", "hp": {{{hp}}}, "max_hp": 80},
            "enemies": {{{enemies}}},
            "incoming_damage": 15, "hand": [],
            "draw_pile": {"count": 7, "cards": {}}, "discard_pile": {"count": 0, "cards": {}}, "exhaust_count": 0,
            "potions": [{{{potion0}}}, {"slot": 1, "name": "Blood Potion", "text": "Heal.", "target": "AnyPlayer", "usable": true}, null],
            "relics": ["Burning Blood"]
          },
          "actions": [{"id": "end_turn", "label": "End turn"}]
        }
        """)!.AsObject();

    private const string Slimes = """
        [{"i": 0, "name": "Leaf Slime (S)", "hp": 3, "max_hp": 9, "intents": ["attack 3"]},
         {"i": 1, "name": "Twig Slime (M)", "hp": 27, "max_hp": 27, "intents": ["attack 11"]}]
        """;

    private const string FirePotion = """{"slot": 0, "name": "Fire Potion", "text": "Deal 20 damage to an enemy.", "target": "AnyEnemy", "usable": true}""";

    private static JsonObject Waiting(int turn, int hp) => JsonNode.Parse($$$"""
        {"version": 200, "screen": "combat", "awaiting_decision": false, "waiting": "enemy turn",
         "state": {"turn": {{{turn}}}, "round": {{{turn}}}, "hp": {{{hp}}}, "block": 5}, "actions": []}
        """)!.AsObject();

    private static JsonObject Busy() => JsonNode.Parse("""
        {"version": 201, "screen": "combat", "awaiting_decision": false, "waiting": "combat ending", "state": {}, "actions": []}
        """)!.AsObject();

    private static JsonObject Rewards(int hp, int floor = 5) => JsonNode.Parse($$$"""
        {"version": 300, "screen": "rewards", "awaiting_decision": true,
         "state": {
           "run": {"act": 1, "floor": {{{floor}}}, "ascension": 0},
           "boss": "Ceremonial Beast",
           "player": {"character": "The Ironclad", "hp": {{{hp}}}, "max_hp": 80, "gold": 120, "relics": ["Burning Blood"], "deck_size": 11, "deck": {"Bash": 1}},
           "potions": [null, {"slot": 1, "name": "Blood Potion", "text": "Heal.", "target": "AnyPlayer", "usable": true}, null],
           "rewards": [{"n": 0, "kind": "gold", "amount": 14}]
         },
         "actions": [{"id": "reward:0", "label": "Take 14 gold"}, {"id": "proceed", "label": "Proceed"}]}
        """)!.AsObject();

    private static JsonObject GameOver(string result = "defeat", int hp = 0) => JsonNode.Parse($$$"""
        {"version": 400, "screen": "game_over", "awaiting_decision": true,
         "state": {
           "result": "{{{result}}}", "run": {"act": 1, "floor": 17, "ascension": 0}, "room": "Boss", "score": 120,
           "player": {"character": "The Ironclad", "hp": {{{hp}}}, "max_hp": 80, "gold": 99, "relics": ["Burning Blood"],
                      "potions": ["Fire Potion", null, null], "deck_size": 20, "deck": {"Bash": 1}}
         },
         "actions": [{"id": "continue", "label": "Continue to the next page of the game over screen"}]}
        """)!.AsObject();

    private static JsonObject Rest(int floor) => JsonNode.Parse($$$"""
        {"version": 500, "screen": "rest", "awaiting_decision": true,
         "state": {
           "run": {"act": 1, "floor": {{{floor}}}, "ascension": 0},
           "hp": 41, "max_hp": 80, "boss": "Ceremonial Beast",
           "player": {"character": "The Ironclad", "gold": 131, "relics": ["Burning Blood", "Golden Pearl"], "deck_size": 13,
                      "deck": {"Bash": 1, "Defend": 4, "Pommel Strike": 1, "Shrug It Off": 1, "Strike": 5, "Thunderclap": 1}},
           "potions": [{"slot": 0, "name": "Blood Potion", "text": "Heal.", "target": "AnyPlayer", "usable": true}, null, null],
           "options": [{"n": 0, "id": "HEAL", "name": "Rest"}, {"n": 1, "id": "SMITH", "name": "Smith"}]
         },
         "actions": [{"id": "option:0", "label": "Rest"}]}
        """)!.AsObject();

    private static JsonObject Map(int floor) => JsonNode.Parse($$$"""
        {"version": 600, "screen": "map", "awaiting_decision": true,
         "state": {"run": {"act": 1, "floor": {{{floor}}}, "ascension": 0}, "player": {"character": "The Ironclad", "hp": 50, "max_hp": 80}},
         "actions": [{"id": "map:0", "label": "Go"}]}
        """)!.AsObject();

    private static JsonObject Acted(string id, JsonObject next) => new() { ["done"] = id, ["next"] = next };

    private JsonObject[] Lines() =>
        File.Exists(_fights.FilePath)
            ? File.ReadAllLines(_fights.FilePath).Select(l => JsonNode.Parse(l)!.AsObject()).ToArray()
            : [];

    private static string[] Names(JsonNode? array) => array!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

    // ---- Tests ----

    [Fact]
    public void A_won_fight_is_one_line_with_everything_the_plan_asks_for()
    {
        // The map pick before the fight isn't counted.
        _fights.Observe(Map(4));
        _fights.Response(0.10m, "medium", cutOff: false);

        _fights.Observe(Board(1, 80, FirePotion));
        Assert.True(_fights.InFight);
        _fights.Response(0.05m, "medium", cutOff: false);
        _fights.Observe(Acted("end_turn", Waiting(1, 80)));
        _fights.Observe(Board(2, 69, FirePotion));
        _fights.Response(0.07m, "medium", cutOff: true);
        _fights.ConversationStarted(refresh: true);
        _fights.Response(0.06m, "medium", cutOff: false);
        // act_sequence: the potion, then a play, from the turn 2 board.
        _fights.Observe(new JsonObject
        {
            ["done"] = new JsonArray(
                new JsonObject { ["id"] = "potion:0>e1", ["label"] = "Use Fire Potion on Twig Slime (M)" },
                new JsonObject { ["id"] = "play:0>e0", ["label"] = "Play Strike on Leaf Slime (S)" }),
            ["next"] = Board(2, 69, enemies: """[{"i": 2, "name": "Twig Slime (S)", "hp": 9, "max_hp": 9, "intents": ["attack 4"]}]"""),
        });
        _fights.Observe(Board(3, 60));
        _fights.Response(0.04m, "medium", cutOff: false);
        _fights.Observe(Acted("end_turn", Busy()));
        Assert.True(_fights.InFight);
        _fights.Observe(Rewards(66));
        Assert.False(_fights.InFight);
        // After the fight: not counted.
        _fights.Response(0.50m, "medium", cutOff: false);

        var line = Assert.Single(Lines());
        Assert.Equal("fight", line["kind"]!.GetValue<string>());
        Assert.Equal("2026-10-11 A1 F5", line["id"]!.GetValue<string>());
        Assert.Equal("The Ironclad", line["character"]!.GetValue<string>());
        Assert.Equal(1, line["act"]!.GetValue<int>());
        Assert.Equal(5, line["floor"]!.GetValue<int>());
        Assert.Equal("Monster", line["room"]!.GetValue<string>());
        Assert.Equal(["Leaf Slime (S)", "Twig Slime (M)", "Twig Slime (S)"], Names(line["enemies"]));
        Assert.Equal("won", line["result"]!.GetValue<string>());
        Assert.Equal("rewards", line["ended_on"]!.GetValue<string>());
        Assert.Equal(80, line["hp_before"]!.GetValue<int>());
        Assert.Equal(80, line["max_hp_before"]!.GetValue<int>());
        Assert.Equal(66, line["hp_after"]!.GetValue<int>()); // after Burning Blood, as the rewards show it
        Assert.Equal(3, line["turns"]!.GetValue<int>());
        Assert.Equal(1, line["seen_from_turn"]!.GetValue<int>());
        Assert.Equal(["Fire Potion"], Names(line["potions_used"]));
        Assert.Empty(line["potions_discarded"]!.AsArray());
        Assert.Equal(["Blood Potion"], Names(line["potions_held"]));
        Assert.Equal(["medium"], Names(line["effort"]));
        Assert.Equal(4, line["requests"]!.GetValue<int>());
        Assert.Equal(1, line["refreshes"]!.GetValue<int>());
        Assert.Equal(1, line["conversations"]!.GetValue<int>());
        Assert.Equal(1, line["cut_offs"]!.GetValue<int>());
        Assert.Equal(0.22m, line["spent_usd"]!.GetValue<decimal>());
        Assert.True(line.ContainsKey("at"));
    }

    [Fact]
    public void A_lost_fight_ends_on_game_over_with_the_potions_still_held()
    {
        _fights.Observe(Board(1, 30, FirePotion, floor: 17, room: "Boss"));
        _fights.Response(0.30m, "high", cutOff: false);
        _fights.Observe(new JsonObject { ["done"] = "discard_potion:1", ["next"] = Board(2, 12, FirePotion, floor: 17, room: "Boss") });
        _fights.Observe(Waiting(2, 0));
        _fights.Observe(GameOver());

        var line = Assert.Single(Lines());
        Assert.Equal("Boss", line["room"]!.GetValue<string>());
        Assert.Equal("lost", line["result"]!.GetValue<string>());
        Assert.Equal("game_over", line["ended_on"]!.GetValue<string>());
        Assert.Equal(0, line["hp_after"]!.GetValue<int>());
        Assert.Equal(["Blood Potion"], Names(line["potions_discarded"]));
        Assert.Equal(["Fire Potion"], Names(line["potions_held"]));
        Assert.Equal(["high"], Names(line["effort"]));
    }

    [Theory]
    [InlineData("abandoned", 54, "abandoned")]
    [InlineData("victory", 54, "won")]
    [InlineData("defeat", 0, "lost")]
    [InlineData("unknown", 54, "unknown")]
    [InlineData("unknown", 0, "lost")]
    public void A_fight_ending_on_game_over_takes_its_result_from_the_game_over(string gameOver, int hp, string result)
    {
        _fights.Observe(Board(1, 60, floor: 17, room: "Boss"));
        _fights.Observe(GameOver(gameOver, hp));

        var line = Assert.Single(Lines());
        Assert.Equal(result, line["result"]!.GetValue<string>());
        Assert.Equal(hp, line["hp_after"]!.GetValue<int>());
    }

    [Fact]
    public void Efforts_are_listed_in_the_order_used_and_a_fresh_conversation_for_effort_is_not_a_refresh()
    {
        _fights.Observe(Board(1, 70, room: "Elite"));
        _fights.ConversationStarted(refresh: false);
        _fights.Response(0.2m, "high", cutOff: false);
        _fights.Response(0.2m, null, cutOff: false);
        _fights.Response(0.2m, "high", cutOff: false);
        _fights.Observe(Rewards(60));

        var line = Assert.Single(Lines());
        Assert.Equal(["high", "default"], Names(line["effort"]));
        Assert.Equal(0, line["refreshes"]!.GetValue<int>());
        Assert.Equal(1, line["conversations"]!.GetValue<int>());
    }

    [Fact]
    public void A_card_choice_mid_fight_and_screens_that_say_nothing_keep_the_fight_open()
    {
        _fights.Observe(Board(1, 70));
        _fights.Observe(JsonNode.Parse("""{"screen": "card_select", "awaiting_decision": true, "state": {"kind": "combat_pile_select", "combat": {"turn": 1}}, "actions": []}"""));
        _fights.Observe(JsonNode.Parse("""{"screen": "modal", "awaiting_decision": true, "state": {}, "actions": []}"""));
        _fights.Observe(JsonNode.Parse("""{"screen": "unknown", "awaiting_decision": false, "state": {}, "actions": []}"""));
        _fights.Observe(JsonNode.Parse("""{"screen": "error", "awaiting_decision": false, "waiting": "reader error", "state": {}, "actions": []}"""));
        Assert.True(_fights.InFight);
        Assert.Empty(Lines());
    }

    [Fact]
    public void A_fight_never_seen_to_end_is_closed_by_the_next_floors_fight()
    {
        _fights.Observe(Board(1, 70, floor: 5));
        _fights.Observe(Board(1, 64, floor: 6));
        _fights.Observe(Rewards(60, floor: 6));

        var lines = Lines();
        Assert.Equal(2, lines.Length);
        Assert.Equal("unknown", lines[0]["result"]!.GetValue<string>());
        Assert.Equal("unseen", lines[0]["ended_on"]!.GetValue<string>());
        Assert.Equal(70, lines[0]["hp_after"]!.GetValue<int>());
        Assert.Equal(6, lines[1]["floor"]!.GetValue<int>());
        Assert.Equal("won", lines[1]["result"]!.GetValue<string>());
    }

    [Fact]
    public void Waiting_snapshots_alone_open_no_fight()
    {
        _fights.Observe(Waiting(1, 80));
        _fights.Observe(Busy());
        Assert.False(_fights.InFight);
        _fights.Observe(Rewards(60));
        Assert.Empty(Lines());
    }

    [Fact]
    public void The_deck_is_written_once_at_the_floor_16_rest_site_and_again_next_run()
    {
        _fights.Observe(Rest(8));
        Assert.Empty(Lines());

        _fights.Observe(Rest(16));
        _fights.Observe(new JsonObject { ["done"] = "option:1", ["next"] = Rest(16) });
        var deck = Assert.Single(Lines());
        Assert.Equal("deck", deck["kind"]!.GetValue<string>());
        Assert.Equal("2026-10-11 A1 F16", deck["id"]!.GetValue<string>());
        Assert.Equal(16, deck["floor"]!.GetValue<int>());
        Assert.Equal("The Ironclad", deck["character"]!.GetValue<string>());
        Assert.Equal(41, deck["hp"]!.GetValue<int>());
        Assert.Equal(13, deck["deck_size"]!.GetValue<int>());
        Assert.Equal(5, deck["deck"]!["Strike"]!.GetValue<int>());
        Assert.Equal(["Burning Blood", "Golden Pearl"], Names(deck["relics"]));
        Assert.Equal(["Blood Potion"], Names(deck["potions"]));

        // A new run: a lower floor, then its own floor 16 rest.
        _fights.Observe(Map(1));
        _fights.Observe(Rest(16));
        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Odd_payloads_never_throw()
    {
        _fights.Observe(null);
        _fights.Observe(new JsonArray(1, 2));
        _fights.Observe(JsonValue.Create("text"));
        _fights.Observe(JsonNode.Parse("""{"screen": 5, "state": []}"""));
        _fights.Observe(JsonNode.Parse("""{"screen": "combat", "state": {"run": "x", "me": {"hp": "lots"}}}"""));
        _fights.Observe(JsonNode.Parse("""{"screen": "combat", "state": {"run": {"act": "one"}, "me": {}, "enemies": [null, 3, {"i": "x"}], "potions": "none"}}"""));
        _fights.Observe(new JsonObject { ["done"] = "potion:99>e0", ["next"] = JsonNode.Parse("""{"screen": "rewards", "state": {"player": {"hp": "x"}}}""") });
        _fights.Observe(JsonNode.Parse("""{"screen": "rest", "state": {"run": {"floor": 16}, "player": {"deck": "x"}}}"""));

        var line = Assert.Single(Lines());
        Assert.Equal(["slot 99"], Names(line["potions_used"]));
        Assert.Empty(_logged);
    }

    [Fact]
    public void A_write_that_fails_is_logged_once_and_not_thrown()
    {
        // The folder is a file, so nothing can be written under it.
        File.WriteAllText(_dir, "not a folder");
        try
        {
            var blocked = new FightLog(Path.Combine(_dir, "sub"), _logged.Add, () => _now);
            for (var i = 0; i < 2; i++)
            {
                blocked.Observe(Board(1, 70));
                blocked.Observe(Rewards(60));
            }
            Assert.Single(_logged, l => l.Contains(FightLog.FileName));
        }
        finally
        {
            File.Delete(_dir);
        }
    }

    [Fact]
    public void A_log_that_throws_is_swallowed_too()
    {
        File.WriteAllText(_dir, "not a folder");
        try
        {
            var blocked = new FightLog(Path.Combine(_dir, "sub"), _ => throw new IOException("log is gone"), () => _now);
            blocked.Observe(Board(1, 70));
            blocked.Observe(Rewards(60));
        }
        finally
        {
            File.Delete(_dir);
        }
    }

    [Fact]
    public void Fight_ids_name_the_local_date_act_and_floor() =>
        Assert.Equal("2026-10-11 A2 F23", FightLog.FightId(new DateTime(2026, 10, 11, 9, 0, 0, DateTimeKind.Local), 2, 23));
}
