using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core.Coop;
using Xunit;

namespace SpireMind.Core.Tests;

public class PartyPlanTests
{
    private static readonly DateTime At = new(2026, 10, 11, 20, 14, 0, DateTimeKind.Local);

    internal static PartyPitch Pitch(string say, string? role = null, string? archetype = null, string[]? wants = null,
        string[]? needs = null, string[]? gives = null, string[]? calls = null, string? question = null) =>
        new(say, role, archetype, wants ?? [], needs ?? [], gives ?? [], calls ?? [], question);

    private static PartySessionResult Session(string key, string kind, params (string, PartyPitch)[] finals) =>
        new(key, kind, int.Parse(key[1..key.IndexOf('-')]), "done", At, "Kin Priest", finals, [], []);

    [Fact]
    public void Read_cuts_every_field_to_its_limit()
    {
        var json = new JsonObject
        {
            ["say"] = new string('s', 300),
            ["role"] = "Debuffer",
            ["archetype"] = new string('a', 100),
            ["wants"] = new JsonArray("1", "2", "3", "4", "5", new string('w', 120)),
            ["needs"] = "front damage",
            ["question"] = new string('q', 200),
        };
        var pitch = PartyPitch.Read(json, out var error)!;
        Assert.Null(error);
        Assert.Equal(PartyPitch.MaxSay, pitch.Say.Length);
        Assert.EndsWith("...", pitch.Say);
        Assert.Equal("debuffer", pitch.Role);
        Assert.Equal(PartyPitch.MaxArchetype, pitch.Archetype!.Length);
        Assert.Equal(["1", "2", "3", "4"], pitch.Wants);
        Assert.Equal(["front damage"], pitch.Needs);
        Assert.Equal(PartyPitch.MaxQuestion, pitch.Question!.Length);

        Assert.Equal("flex", PartyPitch.Read(new JsonObject { ["say"] = "hi", ["role"] = "tank" }, out _)!.Role);
        Assert.Null(PartyPitch.Read(new JsonObject { ["say"] = "  " }, out error));
        Assert.Contains("say is required", error);
    }

    [Fact]
    public void Act_start_replaces_sections_and_calls_and_clears_the_boss_plan()
    {
        var plan = new PartyPlan();
        plan.Merge(Session("A1-boss", PartyTriggers.Boss, ("Silent", Pitch("x", calls: ["Save the Block potion"]))));
        plan.Merge(Session("A2-start", PartyTriggers.ActStart,
            ("Silent", Pitch("poison", "debuffer", "poison with Catalyst", wants: ["Noxious Fumes", "Catalyst"], needs: ["front damage early"],
                gives: ["Weak and Vulnerable first every turn"], calls: ["Damage relics go to Mark"])),
            ("Defect", Pitch("frost", "protector", "frost orbs", calls: ["Mend Mark before elites", "damage relics go to mark"]))));

        Assert.Empty(plan.BossCalls);
        Assert.Equal(["Damage relics go to Mark", "Mend Mark before elites"], plan.Calls.Select(c => c.Text));
        Assert.Equal(["Silent", "Defect"], plan.Calls.Select(c => c.By));
        var silent = plan.Bots.Single(b => b.Label == "Silent");
        Assert.Equal("debuffer", silent.Role);
        Assert.Equal("A2-start", silent.From);
        Assert.Equal(2, plan.Act);
        Assert.Equal("A2-start", plan.LastSession);
    }

    [Fact]
    public void Mid_replaces_only_who_pitched_and_calls_only_when_sent()
    {
        var plan = new PartyPlan();
        plan.Merge(Session("A2-start", PartyTriggers.ActStart,
            ("Silent", Pitch("a", "debuffer", "poison", calls: ["Damage relics go to Mark"])),
            ("Defect", Pitch("b", "protector", "frost"))));
        plan.Merge(Session("A2-mid", PartyTriggers.MidAct, ("Defect", Pitch("c", archetype: "lightning"))));

        Assert.Equal("poison", plan.Bots.Single(b => b.Label == "Silent").Archetype);
        Assert.Equal("A2-start", plan.Bots.Single(b => b.Label == "Silent").From);
        var defect = plan.Bots.Single(b => b.Label == "Defect");
        Assert.Equal("lightning", defect.Archetype);
        // A field the mid pitch left out keeps the old value.
        Assert.Equal("protector", defect.Role);
        Assert.Equal("A2-mid", defect.From);
        Assert.Equal(["Damage relics go to Mark"], plan.Calls.Select(c => c.Text));

        plan.Merge(Session("A2-mid", PartyTriggers.MidAct, ("Silent", Pitch("d", calls: ["Path through the shop"]))));
        Assert.Equal(["Path through the shop"], plan.Calls.Select(c => c.Text));
    }

    [Fact]
    public void Boss_calls_go_to_the_boss_plan_only()
    {
        var plan = new PartyPlan();
        plan.Merge(Session("A2-start", PartyTriggers.ActStart, ("Silent", Pitch("a", "debuffer", "poison", calls: ["Damage relics go to Mark"]))));
        plan.Merge(Session("A2-boss", PartyTriggers.Boss, ("Silent", Pitch("b", "damage", "shivs", calls: ["Silent saves Poison Potion for turn 2"]))));

        Assert.Equal("debuffer", plan.Bots.Single().Role);
        Assert.Equal(["Damage relics go to Mark"], plan.Calls.Select(c => c.Text));
        Assert.Equal(["Silent saves Poison Potion for turn 2"], plan.BossCalls.Select(c => c.Text));
        Assert.Equal(["Damage relics go to Mark (Silent)", "Boss (Kin Priest): Silent saves Poison Potion for turn 2 (Silent)"], plan.CallList());
    }

    [Fact]
    public void Render_is_deterministic_and_shows_every_section()
    {
        var plan = new PartyPlan();
        plan.Merge(new PartySessionResult("A2-start", PartyTriggers.ActStart, 2, "done", At, "Kin Priest",
            [("Silent", Pitch("poison", "debuffer", "poison with Catalyst", wants: ["Noxious Fumes", "Catalyst"], needs: ["front damage"],
                gives: ["Weak first"], calls: ["Damage relics go to Mark"], question: "Want me on Weak duty?"))],
            ["going Strength, can someone bring Weak?"],
            [new PartyQuestion("Silent", "Want me on Weak duty?", "yes", null), new PartyQuestion("Silent", "Shop first?", null, "went shop")]));
        plan.Merge(Session("A2-boss", PartyTriggers.Boss, ("Silent", Pitch("b", calls: ["Save the Block potion for Mark"]))));

        var text = plan.Render("Mark", "Ironclad");
        Assert.Equal(text, plan.Render("Mark", "Ironclad"));
        Assert.StartsWith("# Party plan\nAct 2, last session A2-boss (done) at 20:14.\n\nMark's orders outrank this plan.", text);
        Assert.Contains("## Mark (Ironclad)\n- \"going Strength, can someone bring Weak?\" (A2-start)\n", text);
        Assert.Contains("## Silent (bot)\n- Role: debuffer\n- Archetype: poison with Catalyst\n- Takes: Noxious Fumes, Catalyst\n- Needs: front damage\n- Gives: Weak first\n- From: A2-start\n", text);
        Assert.Contains("## Party calls\n- Damage relics go to Mark (Silent)\n", text);
        Assert.Contains("## Boss plan (Kin Priest)\n- Save the Block potion for Mark (Silent)\n", text);
        Assert.Contains("- Silent asked Mark: \"Want me on Weak duty?\" Mark: \"yes\"", text);
        Assert.Contains("- Silent asked Mark: \"Shop first?\" no answer; Silent guessed: went shop", text);
        Assert.Contains("\n## Mark\n", plan.Render("Mark", null));
    }

    [Fact]
    public void Render_stays_under_the_cap_dropping_old_lines_first()
    {
        var plan = new PartyPlan();
        for (var i = 0; i < 8; i++)
        {
            var finals = Enumerable.Range(0, 3).Select(b => ($"Bot{b}", Pitch("x", "flex", new string('a', 60),
                wants: Enumerable.Repeat(new string('w', 80), 4).ToArray(), needs: Enumerable.Repeat(new string('n', 80), 4).ToArray(),
                gives: Enumerable.Repeat(new string('g', 80), 4).ToArray(), calls: [$"call {i} {b} " + new string('c', 60)]))).ToArray();
            plan.Merge(new PartySessionResult($"A{i + 1}-start", PartyTriggers.ActStart, i + 1, "done", At, null, finals,
                [$"line {i} " + new string('m', 190)], [new PartyQuestion("Bot0", $"question {i} " + new string('q', 120), null, null)]));
        }
        var text = plan.Render("Mark", "Ironclad");
        Assert.True(Encoding.UTF8.GetByteCount(text) <= PartyPlan.MaxBytes, $"{Encoding.UTF8.GetByteCount(text)} bytes");
        // The newest human line outlives the older ones.
        Assert.DoesNotContain("line 0 ", text);
    }

    [Fact]
    public void Yours_is_the_bots_own_section()
    {
        var plan = new PartyPlan();
        plan.Merge(Session("A1-start", PartyTriggers.ActStart, ("Silent", Pitch("a", "debuffer", "poison", wants: ["Catalyst"]))));
        Assert.Equal("Role: debuffer. Archetype: poison. Takes: Catalyst. From: A1-start.", plan.Yours("silent"));
        Assert.Null(plan.Yours("Defect"));
    }

    [Fact]
    public void A_plan_round_trips_through_json()
    {
        var plan = new PartyPlan();
        plan.Merge(new PartySessionResult("A1-start", PartyTriggers.ActStart, 1, "done", At, "Kin Priest",
            [("Silent", Pitch("a", "debuffer", "poison", wants: ["Catalyst"], calls: ["Damage relics go to Mark"]))],
            ["going Strength"], [new PartyQuestion("Silent", "Weak duty?", null, "took it")]));
        var back = PartyPlan.FromJson(JsonNode.Parse(plan.ToJson().ToJsonString()) as JsonObject)!;
        Assert.Equal(plan.Render("Mark", "Ironclad"), back.Render("Mark", "Ironclad"));
        Assert.Null(PartyPlan.FromJson(new JsonObject { ["v"] = 2 }));
    }
}
