using System.Text.Json.Nodes;
using SpireMind.Core.Coop;
using Xunit;

namespace SpireMind.Core.Tests;

public class PartyTriggersTests
{
    internal static JsonObject Run(int act) => new() { ["act"] = act, ["floor"] = 1, ["ascension"] = 0 };

    /// <summary>A map state at row r (no at when r is null), with the boss on bossRow.</summary>
    internal static JsonObject MapState(int act, int? r, int bossRow = 16, string? boss = "Kin Priest")
    {
        var rows = new JsonObject();
        for (var row = r ?? 0; row < bossRow; row++)
            rows[row.ToString()] = row == r ? $"@c2:monster^2,3" : "c2:monster^3 c4:elite^4";
        rows[bossRow.ToString()] = "c3:boss";
        var state = new JsonObject { ["run"] = Run(act), ["map"] = new JsonObject { ["key"] = "k", ["rows"] = rows } };
        if (r is { } here) state["at"] = $"(2,{here}) monster";
        if (boss is not null) state["boss"] = boss;
        return state;
    }

    internal static JsonObject AncientState(int act) => new() { ["run"] = Run(act), ["event"] = "Neow", ["ancient"] = true };

    private static PartyDue? Due(string screen, JsonObject state) =>
        PartyTriggers.Due(new JsonObject { ["screen"] = screen, ["state"] = state });

    [Fact]
    public void An_ancient_event_starts_the_act()
    {
        var due = Due("event", AncientState(2));
        Assert.Equal("A2-start", due!.Key);
        Assert.Equal(PartyTriggers.ActStart, due.Kind);
        Assert.Equal(2, due.Act);
        Assert.Null(Due("event", new JsonObject { ["run"] = Run(2), ["event"] = "Big Fish" }));
    }

    [Fact]
    public void The_act_map_before_its_first_room_starts_the_act()
    {
        var due = Due("map", MapState(3, r: null));
        Assert.Equal("A3-start", due!.Key);
        Assert.Equal(PartyTriggers.ActStart, due.Kind);
    }

    [Fact]
    public void Mid_comes_at_half_the_boss_row_and_boss_two_rows_before_it()
    {
        Assert.Null(Due("map", MapState(1, r: 7, bossRow: 16)));
        Assert.Equal("A1-mid", Due("map", MapState(1, r: 8, bossRow: 16))!.Key);
        Assert.Equal("A1-mid", Due("map", MapState(1, r: 13, bossRow: 16))!.Key);
        Assert.Equal("A1-boss", Due("map", MapState(1, r: 14, bossRow: 16))!.Key);
        Assert.Equal(PartyTriggers.Boss, Due("map", MapState(1, r: 15, bossRow: 16))!.Kind);
        // On or past the boss's row there is nothing left to plan.
        Assert.Null(Due("map", MapState(1, r: 16, bossRow: 16)));
    }

    [Fact]
    public void A_short_act_goes_from_nothing_straight_to_boss()
    {
        // Boss on row 6: mid would need 2r >= 6 and r < 4, so row 3; row 4 is already boss.
        Assert.Equal("A2-mid", Due("map", MapState(2, r: 3, bossRow: 6))!.Key);
        Assert.Equal("A2-boss", Due("map", MapState(2, r: 4, bossRow: 6))!.Key);
    }

    [Fact]
    public void The_lowest_boss_row_counts_and_the_second_boss_is_ignored()
    {
        var state = MapState(3, r: 8, bossRow: 16);
        state["map"]!["rows"]!["17"] = "c3:boss";
        Assert.Equal(16, PartyTriggers.BossRow(state));
    }

    [Theory]
    [InlineData("combat")]
    [InlineData("hand_select")]
    [InlineData("menu")]
    [InlineData("game_over")]
    [InlineData("card_reward")]
    public void Nothing_fires_off_the_map_and_the_ancient(string screen)
    {
        Assert.Null(Due(screen, AncientState(1)));
        Assert.Null(Due(screen, MapState(1, r: 14)));
    }

    [Fact]
    public void Malformed_fields_give_nothing()
    {
        var badAt = MapState(1, r: 8);
        badAt["at"] = "somewhere";
        Assert.Null(Due("map", badAt));

        var noRows = MapState(1, r: 8);
        noRows["map"] = new JsonObject();
        Assert.Null(Due("map", noRows));

        var noBoss = MapState(1, r: 8);
        noBoss["map"]!["rows"]!["16"] = "c3:rest";
        Assert.Null(Due("map", noBoss));

        var badRowKey = MapState(1, r: 8);
        badRowKey["map"]!["rows"] = new JsonObject { ["x"] = "c3:boss" };
        Assert.Null(Due("map", badRowKey));

        Assert.Null(Due("event", new JsonObject { ["ancient"] = true }));
        Assert.Null(Due("event", new JsonObject { ["run"] = new JsonObject { ["act"] = "two" }, ["ancient"] = true }));
        Assert.Null(PartyTriggers.Due(new JsonObject { ["screen"] = "map" }));
        Assert.Null(PartyTriggers.Due(new JsonObject()));
    }
}
