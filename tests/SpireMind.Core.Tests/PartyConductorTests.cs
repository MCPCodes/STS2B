using System.Diagnostics;
using System.Text.Json.Nodes;
using SpireMind.Core.Coop;
using Xunit;

namespace SpireMind.Core.Tests;

public sealed class PartyConductorTests : IDisposable
{
    private DateTime _now = new(2026, 10, 11, 20, 0, 0, DateTimeKind.Utc);
    private readonly List<(int Port, JsonObject Op)> _sent = [];
    private readonly List<string> _announced = [];
    private readonly List<string> _log = [];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-party-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* never made */ }
    }

    private PartyConductor Conductor(PartyTimings? timings = null, string? planFile = null, bool auto = true) =>
        new((port, op) => { lock (_sent) _sent.Add((port, op)); }, line => { lock (_announced) _announced.Add(line); },
            line => { lock (_log) _log.Add(line); }, () => _now, timings, auto, planFile, "Mark", () => "Ironclad");

    private static JsonObject Party(string? due = null, string[]? settled = null, string screen = "event", int act = 1,
        JsonObject? pitch = null, bool auto = true, string? boss = "Kin Priest") => new()
    {
        ["v"] = 1,
        ["auto"] = auto,
        ["due"] = due,
        ["settled"] = new JsonArray((settled ?? []).Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
        ["screen"] = screen,
        ["act"] = act,
        ["boss"] = boss,
        ["holding"] = false,
        ["session"] = null,
        ["pitch"] = pitch,
    };

    private static JsonObject Pitched(string session, int round, string say, string? role = null, string? archetype = null,
        string[]? calls = null, string? question = null)
    {
        var json = PartyPlanTests.Pitch(say, role, archetype, calls: calls, question: question).ToJson();
        json["session"] = session;
        json["round"] = round;
        return json;
    }

    private static void See(PartyConductor c, int port, string label, JsonObject party, string playing = "api", bool paused = false,
        bool? inRun = true, string? session = null) =>
        c.Observe(port, label, session ?? $"s{port}", playing, paused, inRun, party);

    private void Pass(double seconds) => _now += TimeSpan.FromSeconds(seconds);

    private List<(int Port, JsonObject Op)> Ops(string op)
    {
        lock (_sent) return _sent.Where(s => s.Op["op"]!.GetValue<string>() == op).ToList();
    }

    private void ClearSent()
    {
        lock (_sent) _sent.Clear();
    }

    /// <summary>Two bots in act 1 with A1-start due, opened after the gather and through the lead.</summary>
    private PartyConductor TwoBotsInRound1(PartyTimings? timings = null)
    {
        var c = Conductor(timings);
        See(c, 17773, "Silent", Party(due: "A1-start"));
        See(c, 17774, "Defect", Party(due: "A1-start"));
        c.Tick();
        Pass(3);
        c.Tick();
        Pass(10);
        c.Tick();
        return c;
    }

    [Fact]
    public void Opens_after_the_gather_with_only_the_bots_that_play_and_are_not_paused()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-start"));
        See(c, 17774, "Defect", Party(), paused: true);
        See(c, 17775, "Regent", Party(), playing: "none");
        Assert.Equal([17773, 17774, 17775], Ops("hello").Select(h => h.Port));
        Assert.True(Ops("hello")[0].Op["auto"]!.GetValue<bool>());

        c.Tick();
        Pass(2.9);
        c.Tick();
        Assert.Empty(Ops("open"));
        Pass(0.1);
        c.Tick();

        var open = Assert.Single(Ops("open"));
        Assert.Equal(17773, open.Port);
        Assert.Equal("A1-start", open.Op["session"]!.GetValue<string>());
        Assert.Equal(PartyTriggers.ActStart, open.Op["kind"]!.GetValue<string>());
        Assert.Equal(150, open.Op["seconds"]!.GetValue<int>());
        Assert.Equal("A1-start", c.CurrentSession);
        Assert.Equal("Planning for act 1 (up to 2.5 minutes; the bots hold their Ancient picks). Mark, tell them what you're building, or /plan skip.",
            Assert.Single(_announced));
        Assert.Contains(_log, l => l == "party: opening A1-start with Silent");
    }

    [Fact]
    public void Without_a_bot_that_can_plan_the_key_is_settled()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-start"), playing: "none");
        c.Tick();
        Pass(3);
        c.Tick();
        Assert.Empty(Ops("open"));
        Assert.Contains("A1-start", c.Settled);
        Assert.Null(c.CurrentSession);
    }

    [Fact]
    public void Round_one_goes_to_everyone_and_round_two_in_port_order_with_the_earlier_finals()
    {
        var c = TwoBotsInRound1();
        var turns = Ops("turn");
        Assert.Equal([17773, 17774], turns.Select(t => t.Port).Order());
        Assert.All(turns, t =>
        {
            Assert.Equal(1, t.Op["round"]!.GetValue<int>());
            Assert.Equal(2, t.Op["rounds"]!.GetValue<int>());
            Assert.Equal(40, t.Op["seconds"]!.GetValue<int>());
            Assert.StartsWith("Planning session for act 1, round 1 of 2, your turn (40 s).", t.Op["ask"]!.GetValue<string>());
            Assert.Empty(t.Op["said"]!.AsArray());
        });

        ClearSent();
        See(c, 17774, "Defect", Party(pitch: Pitched("A1-start", 1, "Frost here.", "protector", "frost")));
        c.Tick();
        Assert.Empty(Ops("turn"));
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 1, "Poison.", "debuffer", "poison", calls: ["Damage relics go to Mark"])));
        c.Tick();

        var first = Assert.Single(Ops("turn"));
        Assert.Equal(17773, first.Port);
        Assert.Equal(2, first.Op["round"]!.GetValue<int>());
        Assert.StartsWith("Planning session for act 1, final round, your turn (40 s).", first.Op["ask"]!.GetValue<string>());
        Assert.Contains("when you see one. Keep say under 150 characters.", first.Op["ask"]!.GetValue<string>());
        Assert.Equal(["Silent", "Defect"], first.Op["said"]!.AsArray().Select(s => s!["from"]!.GetValue<string>()));

        ClearSent();
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 2, "Poison, final.", "debuffer", "poison with Catalyst", calls: ["Damage relics go to Mark"])));
        c.Tick();
        var second = Assert.Single(Ops("turn"));
        Assert.Equal(17774, second.Port);
        var said = second.Op["said"]!.AsArray();
        Assert.Equal(3, said.Count);
        Assert.Equal(2, said[2]!["round"]!.GetValue<int>());
        Assert.Equal("Poison, final.", said[2]!["pitch"]!["say"]!.GetValue<string>());

        ClearSent();
        See(c, 17774, "Defect", Party(pitch: Pitched("A1-start", 2, "Frost, final.", "protector", "frost", calls: ["Mend Mark before elites"])));
        c.Tick();
        var closes = Ops("close");
        Assert.Equal([17773, 17774], closes.Select(x => x.Port).Order());
        Assert.All(closes, x => Assert.Equal("done", x.Op["reason"]!.GetValue<string>()));
        Assert.Contains("Role: debuffer", closes.Single(x => x.Port == 17773).Op["yours"]!.GetValue<string>());
        Assert.Contains("Archetype: frost", closes.Single(x => x.Port == 17774).Op["yours"]!.GetValue<string>());
        Assert.Equal("Party plan set: Silent poison with Catalyst (debuffer); Defect frost (protector); Damage relics go to Mark; Mend Mark before elites.",
            _announced[^1]);
        Assert.Null(c.CurrentSession);
        Assert.Contains("A1-start", c.Settled);
        Assert.Contains("## Silent (bot)", c.PlanText);
        Assert.Contains("## Party calls\n- Damage relics go to Mark (Silent)\n- Mend Mark before elites (Defect)", c.PlanText);
    }

    [Fact]
    public void One_bot_skips_round_two_unless_it_asked_mark()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-start"));
        c.Tick();
        Pass(3);
        c.Tick();
        // Mark's first line ends the lead.
        c.OnHumanLine("Mark", "going Strength");
        c.Tick();
        var turn = Assert.Single(Ops("turn"));
        Assert.Equal(1, turn.Op["rounds"]!.GetValue<int>());
        Assert.Equal("going Strength", turn.Op["mark"]![0]!["text"]!.GetValue<string>());
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 1, "Weak duty.", "debuffer", "poison")));
        c.Tick();
        Assert.Single(Ops("close"));
        Assert.Single(Ops("turn"));
        Assert.Contains("- \"going Strength\" (A1-start)", c.PlanText);
    }

    [Fact]
    public void A_question_waits_for_marks_answer_and_round_two_follows()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-start"));
        c.Tick();
        Pass(3);
        c.Tick();
        Pass(10);
        c.Tick();
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 1, "Weak duty, Mark?", "debuffer", "poison", question: "Want me on Weak duty?")));
        c.Tick();
        Assert.Single(Ops("turn"));
        Pass(5);
        c.OnHumanLine("Mark", "yes");
        c.Tick();
        var round2 = Ops("turn")[^1];
        Assert.Equal(2, round2.Op["round"]!.GetValue<int>());
        Assert.Contains(PartyPrompts.Answered, round2.Op["ask"]!.GetValue<string>());
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 2, "On it.", "debuffer", "poison")));
        c.Tick();
        Assert.Single(Ops("close"));
        Assert.Contains("- Silent asked Mark: \"Want me on Weak duty?\" Mark: \"yes\"", c.PlanText);
    }

    [Fact]
    public void Without_an_answer_the_wait_ends_and_the_bot_guesses()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-start"));
        c.Tick();
        Pass(3);
        c.Tick();
        Pass(10);
        c.Tick();
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 1, "Weak duty, Mark?", question: "Want me on Weak duty?")));
        c.Tick();
        Pass(29);
        c.Tick();
        Assert.Single(Ops("turn"));
        Pass(1);
        c.Tick();
        Assert.Contains(PartyPrompts.NotAnswered, Ops("turn")[^1].Op["ask"]!.GetValue<string>());
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 2, "Guessed yes: Weak first.")));
        c.Tick();
        Assert.Contains("no answer; Silent guessed: Guessed yes: Weak first.", c.PlanText);
    }

    [Fact]
    public void A_bot_that_doesnt_pitch_in_time_is_passed_over()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-mid", screen: "map"));
        See(c, 17774, "Defect", Party(screen: "map"));
        c.Tick();
        Pass(3);
        c.Tick();
        Assert.Equal("Mid-act check (up to 75 s). /plan skip ends it.", _announced[^1]);
        var turns = Ops("turn");
        Assert.Equal(2, turns.Count);
        Assert.StartsWith("Mid-act check for act 1, your turn (40 s).", turns[0].Op["ask"]!.GetValue<string>());
        Assert.Contains("before the boss (Kin Priest)", turns[0].Op["ask"]!.GetValue<string>());
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-mid", 1, "Still poison.", archetype: "poison")));
        c.Tick();
        Pass(39);
        c.Tick();
        Assert.Empty(Ops("close"));
        Pass(1);
        c.Tick();
        Assert.Contains("Defect didn't answer in time; going on.", _announced);
        Assert.Equal(2, Ops("close").Count);
        Assert.Contains(_log, l => l == "party: Defect didn't pitch in time in A1-mid");
    }

    [Fact]
    public void A_bot_silent_in_round_one_is_not_asked_in_round_two()
    {
        // Silent's brain is stuck: round 2 must not keep handing it the turn while Mark waits.
        var c = TwoBotsInRound1();
        See(c, 17774, "Defect", Party(pitch: Pitched("A1-start", 1, "Frost here.", "protector", "frost")));
        c.Tick();
        ClearSent();
        Pass(40);
        c.Tick();

        Assert.Empty(Ops("turn"));
        Assert.Equal(2, Ops("close").Count);
        Assert.Single(_announced, l => l == "Silent didn't answer in time; going on.");
        Assert.Equal("Party plan set: Defect frost (protector).", _announced[^1]);
        Assert.Null(c.CurrentSession);
    }

    [Fact]
    public void Round_two_goes_on_to_the_next_bot_after_one_is_silent_or_runs_out()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-start"));
        See(c, 17774, "Defect", Party(due: "A1-start"));
        See(c, 17775, "Regent", Party(due: "A1-start"));
        c.Tick();
        Pass(3);
        c.Tick();
        Pass(10);
        c.Tick();
        See(c, 17774, "Defect", Party(pitch: Pitched("A1-start", 1, "Frost here.", "protector", "frost")));
        See(c, 17775, "Regent", Party(pitch: Pitched("A1-start", 1, "Stars.", "damage", "stars")));
        c.Tick();
        ClearSent();
        Pass(40);
        c.Tick();

        // Silent said nothing in round 1, so round 2 starts with Defect.
        Assert.Equal([17774], Ops("turn").Select(t => t.Port));
        // Silent's late round-1 pitch still counts, and Defect runs out of time on its turn.
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 1, "Poison, sorry.", "debuffer", "poison")));
        c.Tick();
        Pass(40);
        c.Tick();
        Assert.Equal([17774, 17775], Ops("turn").Select(t => t.Port));
        Pass(40);
        c.Tick();

        Assert.Equal([17774, 17775], Ops("turn").Select(t => t.Port));
        Assert.Equal(3, Ops("close").Count);
        Assert.Equal(["Silent didn't answer in time; going on.", "Defect didn't answer in time; going on.", "Regent didn't answer in time; going on."],
            _announced.Where(l => l.EndsWith("going on.", StringComparison.Ordinal)));
        Assert.Contains("## Silent (bot)", c.PlanText);
        Assert.Contains("## Defect (bot)", c.PlanText);
        Assert.Contains("## Regent (bot)", c.PlanText);
    }

    [Fact]
    public void The_cap_closes_with_the_pitches_that_came_in()
    {
        var c = TwoBotsInRound1(new PartyTimings { LongCap = TimeSpan.FromSeconds(30) });
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-start", 1, "Poison.", "debuffer", "poison")));
        c.Tick();
        // Opened at 3 s, so the 30 s cap comes at 33 s; round 1 started at 13 s.
        Pass(19);
        c.Tick();
        Assert.Empty(Ops("close"));
        Pass(1);
        c.Tick();
        Assert.Equal(2, Ops("close").Count);
        Assert.Contains("## Silent (bot)", c.PlanText);
        Assert.DoesNotContain("## Defect", c.PlanText);
        Assert.Equal("Party plan set: Silent poison (debuffer).", _announced[^1]);
    }

    [Fact]
    public void Boss_sessions_settle_mid_and_put_calls_in_the_boss_plan()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-boss", screen: "map"));
        c.Tick();
        Pass(3);
        c.Tick();
        Assert.Equal("Boss plan for Kin Priest (up to 75 s). /plan skip ends it.", _announced[^1]);
        Assert.StartsWith("Boss plan for Kin Priest, your turn (40 s).", Ops("turn")[0].Op["ask"]!.GetValue<string>());
        Assert.Contains("A1-mid", c.Settled);
        See(c, 17773, "Silent", Party(pitch: Pitched("A1-boss", 1, "Saving Fire Potion.", calls: ["Silent saves Fire Potion for turn 2"])));
        c.Tick();
        Assert.Equal("Boss plan set: Silent saves Fire Potion for turn 2.", _announced[^1]);
        Assert.Contains("## Boss plan (Kin Priest)", c.PlanText);
    }

    [Fact]
    public void Plan_commands_start_skip_show_and_turn_auto_off_and_on()
    {
        var c = Conductor();
        Assert.Equal(PartyPrompts.NoBots, c.Command("Mark", ""));
        See(c, 17773, "Silent", Party(screen: "combat"));
        Assert.Equal(PartyPrompts.InFight, c.Command("Mark", ""));
        Assert.Equal(PartyPrompts.NoPlan, c.Command("Mark", "show"));
        Assert.Equal(PartyPrompts.NothingToSkip, c.Command("Mark", "skip"));

        See(c, 17773, "Silent", Party(screen: "map"));
        var opening = c.Command("Mark", "");
        Assert.Equal("Planning now (up to 2.5 minutes; the bots hold their card, relic and shop picks). Mark, tell them what you're building, or /plan skip.", opening);
        Assert.Equal("A1-plan1", c.CurrentSession);
        Assert.Equal(PartyTriggers.Manual, Ops("open")[0].Op["kind"]!.GetValue<string>());
        Assert.Equal(PartyPrompts.AlreadyRunning, c.Command("Mark", ""));

        Pass(10);
        c.Tick();
        Assert.StartsWith("Planning session (Mark asked for it), round 1 of 1", Ops("turn")[0].Op["ask"]!.GetValue<string>());
        See(c, 17773, "Silent", Party(screen: "map", pitch: Pitched("A1-plan1", 1, "Poison.", "debuffer", "poison")));
        Assert.Equal(PartyPrompts.Skipped, c.Command("Mark", "skip"));
        var close = Assert.Single(Ops("close"));
        Assert.Equal("Mark skipped it", close.Op["reason"]!.GetValue<string>());
        Assert.Null(c.CurrentSession);
        Assert.Equal("Silent: debuffer; poison.\nNo party calls yet.", c.Command("Mark", "show"));
        Assert.Contains("last session A1-plan1 (skipped)", c.PlanText);

        ClearSent();
        Assert.Equal(PartyPrompts.AutoOff, c.Command("Mark", "off"));
        Assert.False(c.Auto);
        Assert.False(Assert.Single(Ops("hello")).Op["auto"]!.GetValue<bool>());
        See(c, 17773, "Silent", Party(due: "A1-mid", screen: "map", auto: false));
        c.Tick();
        Pass(5);
        c.Tick();
        Assert.Empty(Ops("open"));

        Assert.Equal(PartyPrompts.AutoOn, c.Command("Mark", "on"));
        Assert.True(Ops("hello")[^1].Op["auto"]!.GetValue<bool>());
        Assert.Equal(PartyPrompts.Usage, c.Command("Mark", "dance"));
    }

    [Fact]
    public void A_bot_reporting_another_auto_gets_hello_again()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(auto: false));
        Assert.Single(Ops("hello"));
        Pass(1);
        See(c, 17773, "Silent", Party(auto: false));
        Assert.Single(Ops("hello"));
        Pass(5);
        See(c, 17773, "Silent", Party(auto: false));
        Assert.Equal(2, Ops("hello").Count);
    }

    [Fact]
    public void Keys_the_bots_report_as_settled_are_taken_in()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-start", settled: ["A1-start"]));
        c.Tick();
        Pass(5);
        c.Tick();
        Assert.Empty(Ops("open"));
        Assert.Contains("A1-start", c.Settled);
        // Hello carries the settled keys to a bot that restarts.
        See(c, 17773, "Silent", Party(), session: "restarted");
        var hello = Ops("hello")[^1].Op;
        Assert.Contains("A1-start", hello["settled"]!.AsArray().Select(k => k!.GetValue<string>()));
    }

    [Fact]
    public void The_plan_is_sent_again_on_relink()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A1-mid", screen: "map"));
        c.Tick();
        Pass(3);
        c.Tick();
        See(c, 17773, "Silent", Party(screen: "map", pitch: Pitched("A1-mid", 1, "Poison.", "debuffer", "poison")));
        c.Tick();
        Assert.Single(Ops("close"));

        c.Gone(17773);
        ClearSent();
        See(c, 17773, "Silent", Party(screen: "map"));
        var hello = Assert.Single(Ops("hello")).Op;
        Assert.Contains("# Party plan", hello["plan"]!.GetValue<string>());
        Assert.Equal("Role: debuffer. Archetype: poison. From: A1-mid.", hello["yours"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_plan_is_written_and_a_new_run_archives_it()
    {
        var file = Path.Combine(_dir, "runs", "party", "plan.md");
        var c = Conductor(planFile: file);
        See(c, 17773, "Silent", Party(due: "A1-mid", screen: "map"));
        c.Tick();
        Pass(3);
        c.Tick();
        See(c, 17773, "Silent", Party(screen: "map", pitch: Pitched("A1-mid", 1, "Poison.", "debuffer", "poison")));
        c.Tick();
        await Until(() => File.Exists(file) && File.Exists(Path.Combine(_dir, "runs", "party", "plan.json")));
        Assert.Contains("## Silent (bot)", File.ReadAllText(file));
        Assert.EndsWith("Saved in runs/party/plan.md.", _announced[^1]);
        Assert.Contains(_log, l => l.StartsWith("party: closed A1-mid (done) in 0 s; plan written to ", StringComparison.Ordinal));

        // A restart of the human's copy takes the plan up again.
        var again = Conductor(planFile: file);
        again.Load();
        Assert.Contains("## Silent (bot)", again.PlanText);

        // The bot leaves the run and starts a new one: the plan is put aside.
        ClearSent();
        See(c, 17773, "Silent", Party(screen: "menu"), inRun: false);
        See(c, 17773, "Silent", Party(screen: "event"), inRun: true);
        await Until(() => !File.Exists(file) && Directory.GetFiles(Path.Combine(_dir, "runs", "party"), "2026-*.md").Length == 1);
        Assert.Null(c.PlanText);
        Assert.Empty(c.Settled);
        Assert.Null(Ops("hello")[^1].Op["plan"]);
    }

    [Fact]
    public void One_bot_on_an_old_act_doesnt_put_the_plan_aside_but_all_of_them_do()
    {
        var c = Conductor();
        See(c, 17773, "Silent", Party(due: "A2-mid", screen: "map", act: 2));
        See(c, 17774, "Defect", Party(screen: "map", act: 2));
        c.Tick();
        Pass(3);
        c.Tick();
        See(c, 17773, "Silent", Party(screen: "map", act: 2, pitch: Pitched("A2-mid", 1, "Poison.", "debuffer", "poison")));
        See(c, 17774, "Defect", Party(screen: "map", act: 2, pitch: Pitched("A2-mid", 1, "Frost.", "protector", "frost")));
        c.Tick();
        Assert.NotNull(c.PlanText);

        See(c, 17773, "Silent", Party(screen: "rewards", act: 1));
        Assert.NotNull(c.PlanText);
        See(c, 17774, "Defect", Party(screen: "event", act: 1));
        Assert.Null(c.PlanText);
    }

    [Fact]
    public void A_saved_plan_older_than_three_hours_is_not_taken_up()
    {
        var dir = Path.Combine(_dir, "runs", "party");
        Directory.CreateDirectory(dir);
        var plan = new PartyPlan();
        plan.Merge(new PartySessionResult("A1-start", PartyTriggers.ActStart, 1, "done", _now, null,
            [("Silent", PartyPlanTests.Pitch("a", "debuffer"))], [], []));
        var json = Path.Combine(dir, "plan.json");
        File.WriteAllText(json, plan.ToJson().ToJsonString());
        File.SetLastWriteTimeUtc(json, _now - TimeSpan.FromHours(4));
        var c = Conductor(planFile: Path.Combine(dir, "plan.md"));
        c.Load();
        Assert.Null(c.PlanText);

        File.SetLastWriteTimeUtc(json, _now - TimeSpan.FromHours(1));
        c.Load();
        Assert.NotNull(c.PlanText);
    }

    private static async Task Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
                throw new Xunit.Sdk.XunitException("Timed out.");
            await Task.Delay(10);
        }
    }
}
