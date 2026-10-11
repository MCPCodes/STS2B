using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

// The checks of position tests (arena position): the second file of RunnerChecks.
//
// Nothing here starts Claude Code. A "recording" is made in memory: Greedy plays a scenario and
// every action is written down the way a transcript holds it, one act per action with the
// snapshot after it. The fight itself is the ground truth the replay is held against: the
// snapshot at the start of each turn, the number of actions before it, and how the fight ended.
public sealed partial class RunnerChecks
{
    private void Positions(Check check)
    {
        check.Group("position: the command line", () => PositionOptions(check));
        check.Group("position: reading a transcript", () => Transcript(check));
        check.Group("position: rebuilding a recorded fight", () => Rebuild(check));
        check.Group("position: what the recording says around the actions", () => AroundTheActions(check));
        check.Group("position: an earlier build's display", () => EarlierDisplay(check));
        check.Group("position: a game at the position", () => GameAtPosition(check));
        check.Group("position: the reshuffle", () => Reshuffle(check));
        check.Group("position: the turn's line", () => TurnLine(check));
        check.Group("position: a run's sessions", () => PositionFight(check));
        check.Group("position: the result line", () => PositionLine(check));
        check.Group("position: runs already played", () => PositionSkips(check));
        check.Group("position: the report", () => PositionTables(check));
        check.Group("position: compare", () => PositionCompare(check));
    }

    // ---------- the command line ----------

    private static void PositionOptions(Check check)
    {
        var plain = ArenaOptions.Parse(["position", "runs/pilot", "boss-878005", "claude-x", "--turn", "4"]);
        check.Equal(ArenaCommand.Position, plain.Command, "the position command");
        check.Equal("runs/pilot", plain.PositionSource, "the run the recording is in");
        check.Equal("boss-878005", plain.ScenarioId, "the scenario");
        check.Equal("claude-x", plain.PositionModel, "the model whose fight is rebuilt");
        check.Equal(4, plain.Turn, "--turn");
        check.Equal(5, plain.Runs, "runs unless --runs says otherwise");
        check.Sequence(["claude-x"], plain.Models, "the recorded model plays the position unless --models names others");
        check.That(!plain.Reshuffle && !plain.CheckOnly && !plain.Brief && plain.Experiments == new ExperimentOptions(), "every switch of a position test is off by default");
        check.Equal("base", plain.Experiment, "the label of a position test without a name or a switch");
        check.Equal("medium", plain.Effort, "its effort");

        var all = ArenaOptions.Parse(["position", "--turn", "12", "runs/pilot", "--runs", "3", "elite-1", "--models", "claude-y,claude-z", "claude-x", "--label", "new",
            "--reshuffle", "--effort", "high", "--forecast", "--brief", "--out", "o", "--repo", "r", "--claude", "c", "--parallel", "2", "--timeout-minutes", "5",
            "--grace-seconds", "15", "--stop-at-weekly", "0.5", "--check"]);
        check.Sequence(["runs/pilot", "elite-1", "claude-x"], [all.PositionSource!, all.ScenarioId!, all.PositionModel!], "the three names in the order typed, options between them");
        check.Sequence(["claude-y", "claude-z"], all.Models, "--models names who plays the position");
        check.That(all is { Turn: 12, Runs: 3, Reshuffle: true, CheckOnly: true, Brief: true, Effort: "high", Parallel: 2, OutDir: "o", Repo: "r", ClaudePath: "c" }, "the options as parsed");
        check.Equal("new+brief+forecast", all.Experiment, "the label: the batch's name, then the switches that are on");
        check.Equal("old", ArenaOptions.Parse(["position", "a", "elite-1", "claude-x", "--turn", "2", "--label", "old"]).Experiment, "a name alone");
        check.Equal("forecast", ArenaOptions.Parse(["position", "a", "elite-1", "claude-x", "--turn", "2", "--forecast"]).Experiment, "a switch alone");

        string[] good = ["position", "a", "elite-1", "claude-x", "--turn", "2"];
        foreach (var (bad, why) in new (string[] Args, string Why)[]
                 {
                     (["position", "a", "elite-1", "claude-x"], "no --turn"),
                     (["position", "a", "elite-1", "--turn", "2"], "no model"),
                     (["position", "--turn", "2"], "no names at all"),
                     ([.. good, "claude-y"], "a fourth name"),
                     (["position", "a", "elite-1", "claude-x", "--turn", "0"], "turn 0"),
                     (["position", "a", "elite-1", "claude-x", "--turn", "41"], "a turn past the turn limit"),
                     (["position", "a", "elite-1", "claude-x", "--turn"], "--turn without a value"),
                     ([.. good, "--runs", "0"], "no runs"),
                     ([.. good, "--runs", "51"], "more runs than one command takes"),
                     ([.. good, "--label", "a+b"], "a label with a plus"),
                     ([.. good, "--label", "forecast"], "a label that names a switch"),
                     ([.. good, "--label"], "--label without a value"),
                     ([.. good, "--scenarios", "3"], "--scenarios"),
                     ([.. good, "--seed", "3"], "--seed"),
                     ([.. good, "--effort-by-tier", "boss=high"], "--effort-by-tier"),
                     ([.. good, "--by-label"], "--by-label"),
                     ([.. good, "--models", "claude-y,claude-y"], "a model named twice"),
                 })
            check.That(Refused(bad), $"position is refused with {why}");
        foreach (var option in new[] { "--turn", "--runs", "--label" })
            check.That(Refused(["run", "--models", "claude-x", option, "2"]) && Refused(["report", "a", option, "2"]), $"{option} only goes with position");
        foreach (var option in new[] { "--reshuffle", "--check" })
            check.That(Refused(["run", "--models", "claude-x", option]) && Refused(["compare", "a", "b", option]), $"{option} only goes with position");

        static bool Refused(string[] args)
        {
            try { ArenaOptions.Parse(args); }
            catch (UsageException) { return true; }
            return false;
        }
    }

    // ---------- reading a transcript ----------

    // A snapshot small enough to tell apart by eye.
    private static JsonObject Tiny(int n) => new()
    {
        ["version"] = n, ["screen"] = "combat", ["awaiting_decision"] = true, ["state"] = new JsonObject { ["turn"] = n }, ["actions"] = new JsonArray(),
    };

    // An assistant line with one call to one of the fight's tools, as Claude Code prints it.
    private static string Calls(string id, string tool, JsonObject input) => new JsonObject
    {
        ["type"] = "assistant",
        ["message"] = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = "Thinking aloud." },
                new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = tool, ["input"] = input }),
        },
    }.ToJsonString();

    // The user line that answers a call: the content as a list of text blocks, or as one string.
    private static string Answers(string id, JsonNode payload, bool asString = false)
    {
        var text = payload is JsonValue value && value.TryGetValue<string>(out var plain) ? plain : payload.ToJsonString();
        return new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_result", ["tool_use_id"] = id,
                    ["content"] = asString ? text : new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                }),
            },
        }.ToJsonString();
    }

    private static void Transcript(Check check)
    {
        const string prefix = "mcp__spiremind__";
        string[] lines =
        [
            "{\"type\":\"system\",\"subtype\":\"init\"}",
            "[claude-code:unrecognized_model] a line that isn't JSON",
            Calls("t1", prefix + "wait_for_decision", []),
            Answers("t1", Tiny(1)),
            Calls("t2", prefix + "act", new JsonObject { ["action_id"] = "play:0", ["version"] = 1 }),
            Answers("t2", new JsonObject { ["done"] = "play:0", ["next"] = Tiny(2) }),
            Calls("t3", prefix + "act_sequence", new JsonObject { ["version"] = 2, ["actions"] = new JsonArray("play:0", "play:1", "end_turn") }),
            Answers("t3", new JsonObject
            {
                // The second card moved up a place once the first was played: what ran is not what was sent.
                ["done"] = new JsonArray(new JsonObject { ["id"] = "play:0", ["label"] = "Play Bash on Nibbit" }, new JsonObject { ["id"] = "play:0", ["label"] = "Play Defend" }),
                ["stopped"] = "The screen changed to hand_select.", ["not_run"] = new JsonArray("end_turn"), ["next"] = Tiny(3),
            }),
            Calls("t4", prefix + "act", new JsonObject { ["action_id"] = "play:9", ["version"] = 3 }),
            Answers("t4", new JsonObject { ["error"] = "'play:9' is not a legal action now.", ["current"] = Tiny(3) }, asString: true),
            "{\"type\":\"arena\",\"note\":\"the session ended before the fight was over; resume 1 of at most 5 follows\"}",
            Calls("t5", prefix + "get_state", []),
            Answers("t5", Tiny(3)),
            Calls("t6", "Read", new JsonObject { ["file_path"] = "/x/strategy/wiki/enemies/nibbit.md" }),
            Answers("t6", JsonValue.Create("     1→# Nibbit"), asString: true),
            Calls("t7", prefix + "act", new JsonObject { ["action_id"] = "end_turn", ["version"] = 3 }),
            Answers("zz", Tiny(9)),                                                             // an answer to no call
            Answers("t2", new JsonObject { ["done"] = "play:5", ["next"] = Tiny(8) }),          // a call answered twice
            "",
        ];
        var recording = Recording.FromLines("weak-1", "claude-x", lines);

        check.Sequence(["wait_for_decision", "act", "act_sequence", "act", "get_state", "Read", "act"], recording.Calls.Select(c => c.Tool), "the calls, in the order their results came back, the unanswered one last");
        check.Sequence([4, 6, 8, 10, 13, 15], recording.Calls.Where(c => c.Answered).Select(c => c.Line), "the transcript line of each result");
        check.Equal(1, recording.UnreadableLines, "lines that aren't JSON");
        check.Equal(2, recording.Sessions, "sessions: the first and one resume");

        check.Equal(Tiny(1).ToJsonString(), recording.Calls[0].Snapshot?.ToJsonString(), "wait_for_decision's result is its snapshot");
        check.Sequence([new RanAction("play:0", null)], recording.Calls[1].Ran, "act ran its one id, without a label");
        check.Equal(Tiny(2).ToJsonString(), recording.Calls[1].Snapshot?.ToJsonString(), "act's snapshot is the next one");
        check.Sequence(["play:0", "play:1", "end_turn"], recording.Calls[2].Sent, "what an act_sequence was sent");
        check.Sequence([new RanAction("play:0", "Play Bash on Nibbit"), new RanAction("play:0", "Play Defend")], recording.Calls[2].Ran, "what of it ran: the ids and labels of its result, not the ids sent");
        check.Equal("The screen changed to hand_select.", recording.Calls[2].Stopped, "why it stopped");
        check.Sequence(["end_turn"], recording.Calls[2].NotRun, "what it didn't run");
        check.That(recording.Calls[3].Ran.Count == 0 && recording.Calls[3].Error == "'play:9' is not a legal action now.", "a refused call ran nothing and says why");
        check.Equal(Tiny(3).ToJsonString(), recording.Calls[3].Snapshot?.ToJsonString(), "a refused call's snapshot is the current one (its content was one string)");
        check.Equal(Tiny(3).ToJsonString(), recording.Calls[4].Snapshot?.ToJsonString(), "get_state's result is its snapshot");
        check.That(recording.Calls[5] is { Tool: "Read", Snapshot: null, Ran.Count: 0 }, "a Read is no action and has no snapshot");
        check.That(recording.Calls[6] is { Answered: false, Tool: "act" } && recording.Calls[6].Sent.SequenceEqual(["end_turn"]), "a call without a result is kept as unanswered, with what it sent");
    }

    // ---------- a recording made here ----------

    /// <summary>A fight Greedy played, as a recording, with what the fight itself was at each turn's start.</summary>
    private sealed class Tape
    {
        public required Recording Recording { get; init; }

        /// <summary>By turn: the actions that ran before it, the snapshot at its start, and the player then.</summary>
        public Dictionary<int, (int ActionsBefore, string Snapshot, int Hp, int CardsPlayed)> Turns { get; } = [];

        /// <summary>By turn: the labels of the actions that ran on it, in order.</summary>
        public Dictionary<int, List<string>> Lines { get; } = [];

        public bool Won { get; set; }
        public int HpLeft { get; set; }
        public int LastTurn { get; set; }
        public int CardsPlayed { get; set; }
    }

    private static JsonObject AsRecorded(GameSnapshot snapshot) => (JsonObject)JsonNode.Parse(snapshot.ToJson().ToJsonString())!;

    private static readonly GreedyPolicy Greedy = new();

    // One step of Greedy on a fight; an id the fight turns down gives up the turn, as the baseline does.
    private static (string Id, string Label) Step(Fight fight)
    {
        var snapshot = fight.Snapshot();
        var id = Greedy.Choose(snapshot);
        if (!fight.Execute(id).Ok)
        {
            id = snapshot.Actions.Any(a => a.Id == "end_turn") ? "end_turn" : snapshot.Actions[0].Id;
            fight.Execute(id);
        }
        return (id, snapshot.Actions.First(a => a.Id == id).Label);
    }

    private static Tape Record(string scenarioId)
    {
        var scenario = Scenarios.FromId(scenarioId);
        var fight = new Fight(FightConfig.From(Scenarios.Setup(scenario)));
        var played = new Tape { Recording = new Recording { ScenarioId = scenarioId, Model = "greedy", TranscriptPath = "" } };
        var calls = played.Recording.Calls;
        calls.Add(new RecordedCall { Line = 0, Tool = "wait_for_decision", Snapshot = AsRecorded(fight.Snapshot()) });
        for (var n = 1; n < 6000 && !fight.IsOver; n++)
        {
            var turn = fight.Turn;
            if (!played.Turns.ContainsKey(turn) && !fight.HasPendingChoice)
                played.Turns[turn] = (n - 1, PositionReplay.Json(fight.Snapshot()), fight.Player.Hp, fight.CardsPlayed);
            var (id, label) = Step(fight);
            if (!played.Lines.TryGetValue(turn, out var line))
                played.Lines[turn] = line = [];
            line.Add(label);
            calls.Add(new RecordedCall { Line = n, Tool = "act", Sent = [id], Ran = [new RanAction(id, null)], Snapshot = AsRecorded(fight.Snapshot()) });
        }
        (played.Won, played.HpLeft, played.LastTurn, played.CardsPlayed) = (fight.Won, fight.Player.Hp, fight.Turn, fight.CardsPlayed);
        return played;
    }

    // What the replay says of a recording and a turn: a position, or the sentence it refused with.
    private static (PositionStart? Start, string? Mismatch, string? Missing) Try(Recording recording, int turn)
    {
        try { return (PositionReplay.To(recording, turn), null, null); }
        catch (ReplayMismatch ex) { return (null, ex.Message, null); }
        catch (NoSuchPosition ex) { return (null, null, ex.Message); }
    }

    private static readonly string[] RecordedIds = ["boss-878005", "elite-717141", "normal-280469"];

    private static void Rebuild(Check check)
    {
        foreach (var id in RecordedIds)
        {
            var played = Record(id);
            var last = played.LastTurn;
            check.That(last >= 3, $"{id}: the recorded fight has turns to stop at (it ended on turn {last})");
            foreach (var turn in new[] { 1, 2, (last + 1) / 2, last }.Distinct())
            {
                var (start, mismatch, missing) = Try(played.Recording, turn);
                check.That(start is not null, $"{id} turn {turn}: the recording is rebuilt to the turn ({mismatch ?? missing ?? "ok"})");
                if (start is null)
                    continue;
                var truth = played.Turns[turn];
                check.Equal(truth.Snapshot, start.Snapshot, $"{id} turn {turn}: the position is the fight's own snapshot at that turn's start");
                check.Equal(truth.ActionsBefore, start.Actions.Count, $"{id} turn {turn}: the actions that lead there");
                check.Equal(truth.Hp, start.Hp, $"{id} turn {turn}: the player's HP there");
                check.Equal(truth.CardsPlayed, start.CardsPlayed, $"{id} turn {turn}: the cards played before it");
                // One snapshot before the first action and one after each action applied.
                check.Equal(truth.ActionsBefore + 1, start.SnapshotsChecked, $"{id} turn {turn}: recorded snapshots compared on the way");
                check.Equal(1, start.PositionSnapshots, $"{id} turn {turn}: of them, snapshots of the position itself");
                check.Sequence(played.Lines[turn], start.SourceLine, $"{id} turn {turn}: what the recorded session then did on that turn");
                check.Equal(ArenaPrompt.Hash(truth.Snapshot), start.StateHash, $"{id} turn {turn}: the state hash is the snapshot's");
                check.That(start is { DisplayDifferences: 0, Notes.Count: 0 }, $"{id} turn {turn}: a recording of this build has nothing to remark on");
            }
            check.That(played.Turns.Values.Select(t => ArenaPrompt.Hash(t.Snapshot)).Distinct().Count() == played.Turns.Count, $"{id}: every turn's start has its own state hash");

            // One number changed in a snapshot on the way is caught there; changed after the position, it is not looked at.
            var at = played.Turns[last].ActionsBefore;
            JsonObject Me(Tape p, int call) => (JsonObject)((JsonObject)p.Recording.Calls[call].Snapshot!["state"]!)["me"]!;
            var wrong = Record(id);
            Me(wrong, at)["hp"] = Me(wrong, at)["hp"]!.GetValue<int>() + 1;
            var caught = Try(wrong.Recording, last).Mismatch;
            check.That(caught is not null && caught.Contains("state.me.hp: recorded", StringComparison.Ordinal) && caught.Contains($"Transcript line {at} ", StringComparison.Ordinal),
                $"{id}: one HP changed in the recording's snapshot of the position is caught there ({caught?.Split('\n')[0] ?? "it wasn't"})");
            var early = Record(id);
            var middle = played.Turns[2].ActionsBefore;
            Me(early, middle)["hp"] = Me(early, middle)["hp"]!.GetValue<int>() + 1;
            check.That(Try(early.Recording, last).Mismatch is { } m && m.Contains($"Transcript line {middle} ", StringComparison.Ordinal), $"{id}: one HP changed turns before the position is caught where it stands");
            check.That(Try(early.Recording, 1).Start is not null, $"{id}: and a turn before that snapshot is still rebuilt");
            var later = Record(id);
            var after = played.Turns[2].ActionsBefore + 1;
            Me(later, after)["hp"] = Me(later, after)["hp"]!.GetValue<int>() + 1;
            check.Equal(played.Turns[2].Snapshot, Try(later.Recording, 2).Start?.Snapshot, $"{id}: a snapshot after the position is not what is being checked");

            // One action swapped for another.
            var swapped = Record(id);
            var play = swapped.Recording.Calls.FindIndex(c => c.Ran.Count == 1 && c.Ran[0].Id.StartsWith("play:", StringComparison.Ordinal));
            swapped.Recording.Calls[play] = swapped.Recording.Calls[play] with { Ran = [new RanAction("end_turn", null)] };
            check.That(Try(swapped.Recording, last).Mismatch is not null, $"{id}: one action swapped in the recording is caught");

            // Turns the fight never had are no position, and no mismatch either.
            var beyond = Try(played.Recording, last + 1);
            check.That(beyond is { Start: null, Mismatch: null } && beyond.Missing!.Contains($"has no turn {last + 1}: the recorded fight was over on turn {last}", StringComparison.Ordinal),
                $"{id}: a turn after the fight's last is said to be missing ({beyond.Missing ?? beyond.Mismatch ?? "it was rebuilt"})");
            check.That(Try(played.Recording, 0).Missing is not null, $"{id}: there is no turn 0");

            // A transcript that ends in the middle of the fight has the turns it reached, and the one it stopped on.
            var cut = Record(id);
            cut.Recording.Calls.RemoveRange(played.Turns[3].ActionsBefore + 1, cut.Recording.Calls.Count - played.Turns[3].ActionsBefore - 1);
            var stopped = Try(cut.Recording, 3).Start;
            check.That(stopped is not null && stopped.Snapshot == played.Turns[3].Snapshot && stopped.SourceLine.Count == 0, $"{id}: a recording that ends at a turn's start still gives that position, with nothing played on it");
            check.That(Try(cut.Recording, 4).Missing is { } running && running.Contains("the recording ends on turn 3 with the fight still running", StringComparison.Ordinal),
                $"{id}: and no later one");
        }
    }

    private static void AroundTheActions(Check check)
    {
        const string id = "normal-280469";
        var truth = Record(id);
        var last = truth.LastTurn;
        var fight = new Fight(FightConfig.From(Scenarios.Setup(Scenarios.FromId(id))));
        JsonObject Busy(int hpOff) => new()
        {
            ["version"] = 1, ["screen"] = "combat", ["awaiting_decision"] = false, ["waiting"] = "resolving",
            ["state"] = new JsonObject { ["turn"] = fight.Turn, ["round"] = fight.Round, ["hp"] = fight.Player.Hp + hpOff, ["block"] = fight.Player.Block },
        };

        // A busy snapshot (four keys only), a refused call (nothing ran, the current snapshot) and a call with no answer.
        var made = Record(id);
        var first = made.Recording.Calls[0].Snapshot!;
        made.Recording.Calls.Insert(1, new RecordedCall { Line = 0, Tool = "get_state", Snapshot = Busy(0) });
        made.Recording.Calls.Insert(2, new RecordedCall { Line = 0, Tool = "act", Sent = ["play:9"], Error = "'play:9' is not a legal action now.", Snapshot = (JsonObject)first.DeepClone() });
        made.Recording.Calls.Add(new RecordedCall { Line = 0, Tool = "act_sequence", Sent = ["done"], Answered = false });
        var ok = Try(made.Recording, last);
        check.That(ok.Start is not null && ok.Start.Snapshot == truth.Turns[last].Snapshot, $"a busy snapshot, a refused call and a call with no answer do not disturb the rebuild ({ok.Mismatch ?? ok.Missing ?? "ok"})");
        check.Equal(truth.Turns[last].ActionsBefore + 3, ok.Start?.SnapshotsChecked ?? -1, "the busy snapshot and the refused call's are compared as well");
        // On turn 1 the refused call's snapshot is one more of the position itself; the busy one is not a full snapshot.
        check.Equal(2, Try(made.Recording, 1).Start?.PositionSnapshots ?? -1, "full snapshots of turn 1's start in that recording");

        var wrong = Record(id);
        wrong.Recording.Calls.Insert(1, new RecordedCall { Line = 0, Tool = "get_state", Snapshot = Busy(1) });
        check.That(Try(wrong.Recording, last).Mismatch is { } m && m.Contains("state.hp", StringComparison.Ordinal), "a busy snapshot with another HP is caught");

        // A turn whose start the recording never saw whole: only a busy snapshot stands there.
        var blind = Record(id);
        var second = truth.Turns[2].ActionsBefore;
        fight = new Fight(FightConfig.From(Scenarios.Setup(Scenarios.FromId(id))));
        foreach (var call in truth.Recording.Calls.Skip(1).Take(second)) fight.Execute(call.Ran[0].Id);
        blind.Recording.Calls[second] = blind.Recording.Calls[second] with { Snapshot = Busy(0) };
        blind.Recording.Calls.RemoveRange(second + 1, blind.Recording.Calls.Count - second - 1);
        check.That(Try(blind.Recording, 2).Missing is { } none && none.Contains("holds no snapshot of the start of turn 2", StringComparison.Ordinal),
            "a turn the recording holds no full snapshot of is not handed on");

        // A sequence: several actions under one result, labels given, the snapshot only at the end.
        var seq = Record(id);
        var acts = seq.Recording.Calls.Skip(1).Take(3).ToList();
        var labelFight = new Fight(FightConfig.From(Scenarios.Setup(Scenarios.FromId(id))));
        var ran = new List<RanAction>();
        foreach (var a in acts)
        {
            ran.Add(new RanAction(a.Ran[0].Id, labelFight.Snapshot().Actions.First(x => x.Id == a.Ran[0].Id).Label));
            labelFight.Execute(a.Ran[0].Id);
        }
        seq.Recording.Calls.RemoveRange(1, 3);
        seq.Recording.Calls.Insert(1, new RecordedCall
        {
            Line = 0, Tool = "act_sequence", Sent = ran.Select(r => r.Id).ToList(), Ran = ran, Snapshot = acts[^1].Snapshot, Stopped = "The screen changed.", NotRun = ["end_turn"],
        });
        var seqOk = Try(seq.Recording, last);
        check.That(seqOk.Start is not null && seqOk.Start.Snapshot == truth.Turns[last].Snapshot, $"a sequence of 3 actions under one result is applied ({seqOk.Mismatch ?? seqOk.Missing ?? "ok"})");
        check.Equal(3, seqOk.Start?.LabelsChecked ?? -1, "its three labels are compared");
        check.Equal(truth.Turns[last].ActionsBefore + 1 - 2, seqOk.Start?.SnapshotsChecked ?? -1, "and one snapshot stands for the three actions");

        seq.Recording.Calls[1] = seq.Recording.Calls[1] with { Ran = [ran[0], new RanAction(ran[1].Id, "Play Something Else"), ran[2]] };
        check.That(Try(seq.Recording, last).Mismatch is { } lm && lm.Contains("Play Something Else", StringComparison.Ordinal), "a sequence whose recorded label names another card is caught at that action");
    }

    // The first recordings were made by a build that named the player "Ironclad" and laid the card
    // choice screen's small picture of the fight out differently. Both are let through, and nothing else.
    private static void EarlierDisplay(Check check)
    {
        static void Rename(Tape played, string name)
        {
            foreach (var call in played.Recording.Calls)
            {
                var state = call.Snapshot?["state"] as JsonObject;
                foreach (var me in new[] { state?["me"] as JsonObject, (state?["combat"] as JsonObject)?["me"] as JsonObject })
                    if (me?["name"] is not null) me["name"] = name;
            }
        }

        const string id = "boss-878005";
        var truth = Record(id);
        var last = truth.LastTurn;

        var earlier = Record(id);
        Rename(earlier, PositionReplay.EarlierPlayerName);
        var named = Try(earlier.Recording, last);
        check.Equal(truth.Turns[last].Snapshot, named.Start?.Snapshot, $"a recording that names the player Ironclad is rebuilt ({named.Mismatch ?? named.Missing ?? "ok"})");
        check.Equal(named.Start?.SnapshotsChecked ?? -1, named.Start?.DisplayDifferences ?? -2, "every snapshot of it shows the earlier name");
        check.That(named.Start?.Notes.Count == 1 && named.Start.Notes[0].StartsWith("the recording names the player Ironclad (in ", StringComparison.Ordinal), "and the position says so in a note");
        check.That(named.Start?.Snapshot.Contains("\"name\":\"The Ironclad\"", StringComparison.Ordinal) == true, "the position itself carries this build's name");

        var stranger = Record(id);
        Rename(stranger, "Silent");
        check.That(Try(stranger.Recording, last).Mismatch is { } other && other.Contains("state.me.name: recorded \"Silent\", rebuilt \"The Ironclad\"", StringComparison.Ordinal),
            "any other name is a mismatch");

        // A card choice: the Power Potion in slot 1 offers three cards (card_select), as it did in Opus's own fight.
        Recording Choice(Action<JsonObject>? choiceScreen = null, Action<JsonObject>? combatAfter = null)
        {
            var fight = new Fight(FightConfig.From(Scenarios.Setup(Scenarios.FromId(id))));
            var recording = new Recording { ScenarioId = id, Model = "by-hand", TranscriptPath = "" };
            recording.Calls.Add(new RecordedCall { Line = 1, Tool = "wait_for_decision", Snapshot = AsRecorded(fight.Snapshot()) });
            var n = 1;
            foreach (var action in new[] { "potion:1", "card:0", "end_turn" })
            {
                fight.Execute(action);
                var snapshot = AsRecorded(fight.Snapshot());
                if (action == "potion:1") choiceScreen?.Invoke(snapshot);
                if (action == "card:0") combatAfter?.Invoke(snapshot);
                recording.Calls.Add(new RecordedCall { Line = ++n, Tool = "act", Sent = [action], Ran = [new RanAction(action, null)], Snapshot = snapshot });
            }
            return recording;
        }

        // The picture as the earlier build drew it: hand cards by name, enemies without their index, two counts fewer.
        static void EarlierPicture(JsonObject snapshot)
        {
            var picture = (JsonObject)((JsonObject)snapshot["state"]!)["combat"]!;
            picture["hand"] = new JsonArray(((JsonArray)picture["hand"]!).Select(card => (JsonNode)JsonValue.Create(FightResult.Str(card!["name"]))!).ToArray());
            foreach (var enemy in (JsonArray)picture["enemies"]!) ((JsonObject)enemy!).Remove("i");
            picture.Remove("exhaust_count");
            picture.Remove("incoming_damage");
        }
        static JsonObject Picture(JsonObject snapshot) => (JsonObject)((JsonObject)snapshot["state"]!)["combat"]!;

        var plain = Choice();
        check.Equal("card_select", FightResult.Str(plain.Calls[1].Snapshot!["screen"]), "the Power Potion opens a card choice");
        check.That(Picture(plain.Calls[1].Snapshot!)["hand"] is JsonArray { Count: > 0 } hand && hand[0] is JsonObject, "whose small picture lists the hand's cards as objects in this build");
        check.That(Try(plain, 2).Start is { DisplayDifferences: 0 }, "a choice screen of this build is compared whole and has nothing to remark on");

        var drawn = Try(Choice(EarlierPicture), 2);
        check.That(drawn.Start is { DisplayDifferences: 1, Notes.Count: 1 } && drawn.Start.Notes[0].Contains("small picture of the fight (state.combat) is laid out differently", StringComparison.Ordinal),
            $"the earlier layout of that picture is let through and remarked on ({drawn.Mismatch ?? drawn.Missing ?? string.Join(" | ", drawn.Start?.Notes ?? [])})");

        var wrongHp = Try(Choice(snapshot =>
        {
            EarlierPicture(snapshot);
            var me = (JsonObject)Picture(snapshot)["me"]!;
            me["hp"] = me["hp"]!.GetValue<int>() + 1;
        }), 2).Mismatch;
        check.That(wrongHp is not null && wrongHp.Contains("state.combat.me.hp", StringComparison.Ordinal), "a number that differs inside the picture is still a mismatch");
        var fewer = Try(Choice(snapshot =>
        {
            EarlierPicture(snapshot);
            ((JsonArray)Picture(snapshot)["enemies"]!).RemoveAt(0);
        }), 2).Mismatch;
        check.That(fewer is not null && fewer.Contains("state.combat.enemies: recorded has 2 entries, rebuilt 3", StringComparison.Ordinal), "so is an enemy missing from it");
        var otherCard = Try(Choice(snapshot => ((JsonObject)((JsonArray)((JsonObject)snapshot["state"]!)["cards"]!)[0]!)["name"] = "Another Card"), 2).Mismatch;
        check.That(otherCard is not null && otherCard.Contains("state.cards[0].name", StringComparison.Ordinal), "the choice itself is compared whole: another card on offer is a mismatch");
        var missingKey = Try(Choice(combatAfter: snapshot => ((JsonObject)snapshot["state"]!).Remove("exhaust_count")), 2).Mismatch;
        check.That(missingKey is not null && missingKey.Contains("state.exhaust_count: recorded has no such key", StringComparison.Ordinal), "and on a combat snapshot a missing key is a mismatch as ever");
    }

    // ---------- a game at the position ----------

    // Greedy plays a game to its end, without waiting.
    private static (bool Won, int Hp, int Turn, int Cards) Finish(ArenaGame game)
    {
        for (var n = 0; n < 6000 && !game.IsOver; n++)
        {
            var snapshot = game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
            if (!game.ExecuteAsync(Greedy.Choose(snapshot), CancellationToken.None).GetAwaiter().GetResult().Ok)
                game.ExecuteAsync(snapshot.Actions.Any(a => a.Id == "end_turn") ? "end_turn" : snapshot.Actions[0].Id, CancellationToken.None).GetAwaiter().GetResult();
        }
        return (game.Won, game.PlayerHp, game.Turn, game.CardsPlayed);
    }

    private static string Shown(ArenaGame game) => PositionReplay.Json(game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult());

    private static void GameAtPosition(Check check)
    {
        foreach (var id in RecordedIds)
        {
            var played = Record(id);
            foreach (var turn in new[] { 1, 2, played.LastTurn })
            {
                var start = PositionReplay.To(played.Recording, turn);
                var game = start.NewGame(new ExperimentOptions(), null, TimeSpan.Zero);
                check.That(game.Turn == turn && game.PlayerHp == start.Hp && game.CardsPlayed == start.CardsPlayed && !game.IsOver, $"{id} turn {turn}: a new game stands at the position");
                check.Equal(0, game.RefusedActions, $"{id} turn {turn}: with no action refused on the way");
                check.Equal(start.Snapshot, Shown(game), $"{id} turn {turn}: its first snapshot is the position's, awaiting a decision");
                // Played on by the same player, the recorded deal ends exactly as the recorded fight did.
                check.Equal((played.Won, played.HpLeft, played.LastTurn, played.CardsPlayed), Finish(game), $"{id} turn {turn}: played on by the same player, the fight ends as the recorded one did");
            }

            var second = PositionReplay.To(played.Recording, 2);
            // The game a session gets is busy after an action, as any fight's.
            var live = second.NewGame(new ExperimentOptions(), null);
            check.Equal(ArenaGame.DefaultActionDelay, live.ActionDelay, $"{id}: a run's game has a fight's usual busy spell");
            check.Equal(second.Snapshot, Shown(live), $"{id}: and is not busy when the session first looks");

            // A position that doesn't lead where it says is refused, never played.
            string? Refusal(PositionStart start)
            {
                try { start.NewGame(new ExperimentOptions(), null); }
                catch (ReplayMismatch ex) { return ex.Message; }
                return null;
            }
            check.That(Refusal(second with { Actions = second.Actions.Take(second.Actions.Count - 1).ToList() }) is { } shortOf && shortOf.Contains("did not reach the same position", StringComparison.Ordinal),
                $"{id}: one action short of the position is refused");
            check.That(Refusal(second with { Actions = [.. second.Actions.Take(second.Actions.Count - 1), "play:99"] }) is { } illegal && illegal.Contains("'play:99') was refused", StringComparison.Ordinal),
                $"{id}: an action the fight turns down on the way is refused");
        }
    }

    private static void Reshuffle(Check check)
    {
        // The seed of a run's deal follows from the scenario, the turn and the run alone.
        check.Equal(PositionStart.DealSeed("boss-878005", 4, 1), PositionStart.DealSeed("boss-878005", 4, 1), "the same run's deal seed twice");
        var seeds = new[]
        {
            PositionStart.DealSeed("boss-878005", 4, 1), PositionStart.DealSeed("boss-878005", 4, 2), PositionStart.DealSeed("boss-878005", 5, 1),
            PositionStart.DealSeed("boss-170286", 4, 1), PositionStart.DealSeed("boss-878005", 4, 11),
        };
        check.Equal(seeds.Length, seeds.Distinct().Count(), "another run, turn or scenario is another seed");
        check.That(Enumerable.Range(1, 50).All(run => PositionStart.DealSeed("elite-780979", 2, run) >= 0), "a deal seed is never negative");

        foreach (var id in RecordedIds)
        {
            var played = Record(id);
            var start = PositionReplay.To(played.Recording, 1);
            var recorded = start.NewGame(new ExperimentOptions(), null, TimeSpan.Zero);
            static List<string> Draw(ArenaGame game) => game.Fight.DrawPile.Select(card => card.Title).ToList();
            static List<string> Hand(ArenaGame game) => game.Fight.Hand.Select(card => card.Title).ToList();
            check.That(Draw(recorded).Count >= 4, $"{id}: turn 1 has a draw pile worth shuffling ({Draw(recorded).Count} cards)");

            var orders = new List<string>();
            var ends = new List<(bool, int, int, int)>();
            foreach (var run in Enumerable.Range(1, 6))
            {
                var seed = PositionStart.DealSeed(id, 1, run);
                var game = start.NewGame(new ExperimentOptions(), seed, TimeSpan.Zero);
                check.Equal(start.Snapshot, Shown(game), $"{id} deal {run}: the first snapshot is the position's, to the letter");
                check.Sequence(Hand(recorded), Hand(game), $"{id} deal {run}: the hand is the recorded one");
                check.Sequence(Draw(recorded).Order(StringComparer.Ordinal), Draw(game).Order(StringComparer.Ordinal), $"{id} deal {run}: the draw pile holds the same cards");
                // It is the engine's own re-deal: the copy CloneDeterminized makes of the fight at the position.
                check.Sequence(recorded.Fight.CloneDeterminized(seed).DrawPile.Select(card => card.Title), Draw(game), $"{id} deal {run}: in the order the engine's own copy has for that seed");
                orders.Add(string.Join(",", Draw(game)));

                // The same seed is the same deal: two games end alike, card for card.
                var again = start.NewGame(new ExperimentOptions(), seed, TimeSpan.Zero);
                check.Sequence(Draw(game), Draw(again), $"{id} deal {run}: the same seed deals the same order again");
                var end = Finish(game);
                check.Equal(end, Finish(again), $"{id} deal {run}: and the same player ends the same way on it");
                ends.Add(end);
            }
            check.That(orders.Any(order => order != string.Join(",", Draw(recorded))), $"{id}: a reshuffled deal's draw order differs from the recorded one");
            check.That(orders.Distinct().Count() > 1, $"{id}: and the deals differ from each other");
            check.That(ends.Distinct().Count() > 1 || ends[0] != (played.Won, played.HpLeft, played.LastTurn, played.CardsPlayed),
                $"{id}: so the same player does not end every deal as it ended the recorded one");
        }

        // A later turn, with cards in the discard pile and enemies that have moved: the same holds.
        var late = Record("boss-878005");
        var third = PositionReplay.To(late.Recording, 3);
        var dealt = third.NewGame(new ExperimentOptions(), PositionStart.DealSeed("boss-878005", 3, 1), TimeSpan.Zero);
        check.Equal(third.Snapshot, Shown(dealt), "turn 3 reshuffled: the first snapshot is the position's");
        check.That(dealt.Turn == 3 && dealt.PlayerHp == third.Hp && dealt.CardsPlayed == third.CardsPlayed && dealt.RefusedActions == 0, "and the game stands at the position");
    }

    // ---------- what a run's session did on the position's turn ----------

    private static void TurnLine(Check check)
    {
        var played = Record("normal-280469");
        var start = PositionReplay.To(played.Recording, 2);
        var game = start.NewGame(new ExperimentOptions(), null, TimeSpan.Zero);
        var log = new ActionLog(game, 2);
        ActionResult Run(string id) => log.ExecuteAsync(id, CancellationToken.None).GetAwaiter().GetResult();
        GameSnapshot Look() => log.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();

        check.Equal(start.Snapshot, PositionReplay.Json(Look()), "the log hands the game's snapshots through untouched");
        check.That(log.Labels.Count == 0 && !log.Complete, "nothing ran yet");

        // The turn as Greedy plays it, through the log: every action by id and by its label of the moment.
        var expected = new List<(string Id, string Label)>();
        while (!game.IsOver && game.Turn == 2)
        {
            var snapshot = Look();
            var id = Greedy.Choose(snapshot);
            if (expected.Count == 1)
            {
                check.That(!Run("play:99").Ok, "an action the game refuses");
                check.Equal(1, log.Ids.Count, "is not in the line");
                check.Equal(1, game.RefusedActions, "though the game counted it, as for any fight");
            }
            check.That(Run(id).Ok, $"'{id}' runs through the log");
            expected.Add((id, snapshot.Actions.First(a => a.Id == id).Label));
        }
        check.Sequence(expected.Select(e => e.Id), log.Ids, "the ids that ran on the turn, in order");
        check.Sequence(expected.Select(e => e.Label), log.Labels, "and their labels");
        check.Sequence(played.Lines[2], log.Labels, "which is the line the recorded fight had on that turn, since the same player played it");
        check.Equal("End turn", log.Labels[^1], "the closing end_turn is part of it");
        check.That(log.Complete, "and the turn is behind the fight");

        var before = log.Ids.Count;
        if (!game.IsOver)
        {
            Run(Greedy.Choose(Look()));
            check.Equal(before, log.Ids.Count, "what runs on a later turn is not in the line");
        }

        // A session that stops in the middle of the turn leaves the turn's beginning, marked unfinished.
        var half = start.NewGame(new ExperimentOptions(), null, TimeSpan.Zero);
        var halfLog = new ActionLog(half, 2);
        halfLog.ExecuteAsync(expected[0].Id, CancellationToken.None).GetAwaiter().GetResult();
        check.That(halfLog.Labels.SequenceEqual([expected[0].Label]) && !halfLog.Complete, "one action and no end_turn: one label, and the turn not ended");

        // The words a table shows for a line.
        check.Equal("Defend, Defend, Cinder on Vantom", PositionInfo.Words(["Play Defend", "Play Defend", "Play Cinder on Vantom", "End turn"]), "a line in words: cards without \"Play\", the closing \"End turn\" left off");
        check.Equal("Use Skill Potion, Take Flame Barrier, Flame Barrier", PositionInfo.Words(["Use Skill Potion", "Take Flame Barrier", "Play Flame Barrier"]), "potions and picks keep their words");
        check.Equal("(ended the turn at once)", PositionInfo.Words(["End turn"]), "a turn ended with nothing played");
        check.Equal("(ended the turn at once)", PositionInfo.Words([]), "and a turn on which nothing ran at all");
    }

    // What a position run's sessions are started with, through the launcher that starts nothing.
    private static void PositionFight(Check check)
    {
        using var checkout = new Checkout(VantomPage);
        var repo = checkout.Path;
        var played = Record("boss-878005");
        var start = PositionReplay.To(played.Recording, 3) with { SourceFolder = "/runs/pilot", SourceModel = "claude-opus-5-5", SourceOutcome = Outcomes.Won, SourceHpLeft = 11 };
        var seed = PositionStart.DealSeed("boss-878005", 3, 2);

        // The first message: the plain one with one sentence more in its first paragraph.
        var told = ArenaPrompt.PositionUserPrompt(3);
        const string sentence = " This fight is already under way: you take it over at the start of turn 3, and the turns before it cannot be changed. Your first snapshot shows where the fight stands.";
        check.That(told.Contains(sentence, StringComparison.Ordinal), "the first message says the fight is under way and from which turn");
        check.Equal(ArenaPrompt.UserPrompt, told.Replace(sentence, ""), "and is the plain first message otherwise");
        check.Equal(ArenaPrompt.UserPrompt.IndexOf("\n\n", StringComparison.Ordinal), told.IndexOf(sentence, StringComparison.Ordinal), "the sentence ends the first paragraph");

        var launcher = new NoLauncher();
        var fight = new ClaudeFight
        {
            Scenario = start.Scenario, Model = "claude-x", Effort = "medium", Experiment = "new",
            Position = start, PositionRun = 2, DealSeed = seed, UserPrompt = told,
            ClaudePath = "/nowhere/claude", SystemPrompt = ArenaPrompt.Build(repo).Text, Repo = repo, PromptHash = "0123456789ab",
            Environment = new Dictionary<string, string?>(), Launcher = launcher,
        };
        var game = fight.NewGame();
        check.That(game.Turn == 3 && game.PlayerHp == start.Hp, "the run's game stands at the position");
        check.Equal(new ExperimentOptions(), game.Experiments, "a position run without a switch has the simulator's switches off");
        check.Equal(start.Snapshot, PositionReplay.Json(game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult()), "and its first snapshot is the position's, to the letter");

        // The simulator's switches reach a position run's game as they reach a whole fight's. With
        // --forecast the first snapshot a session sees carries the forecast; take its two keys
        // out and it is the position's own snapshot, so the state hash names the same position
        // under every label.
        var forecasting = new ClaudeFight
        {
            Scenario = start.Scenario, Model = "claude-x", Effort = "medium", Experiment = "new+forecast",
            Position = start, PositionRun = 2, DealSeed = seed, UserPrompt = told, Experiments = new ExperimentOptions(Forecast: true),
            ClaudePath = "/nowhere/claude", SystemPrompt = ArenaPrompt.Build(repo).Text, Repo = repo, PromptHash = "0123456789ab",
            Environment = new Dictionary<string, string?>(), Launcher = new NoLauncher(),
        };
        var forecastGame = forecasting.NewGame();
        check.Equal(new ExperimentOptions(Forecast: true), forecastGame.Experiments, "--forecast reaches a position run's game");
        var seen = forecastGame.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
        var enemies = seen.State["enemies"] as JsonArray ?? [];
        check.That(seen.State[Forecast.HintKey] is not null && enemies.Count > 0 && enemies.All(e => e?[Forecast.NextKey] is JsonArray { Count: > 0 }),
            "its first snapshot carries next for every enemy, and the note");
        seen.State.Remove(Forecast.HintKey);
        foreach (var enemy in enemies) (enemy as JsonObject)?.Remove(Forecast.NextKey);
        check.Equal(start.Snapshot, PositionReplay.Json(seen), "without the two keys it is the position's snapshot, so the state hash is the same with the switch and without");

        var result = fight.RunAsync(CancellationToken.None).GetAwaiter().GetResult();

        check.Equal(1 + ClaudeFight.MaxResumes, launcher.Started.Count, "sessions started: the first and every resume");
        if (launcher.Started.Count > 1)
        {
            check.Equal(told, Values(launcher.Started[0].Arguments.ToList(), "-p").Single(), "the first session gets the position's first message");
            var resumed = launcher.Started[1].Arguments.ToList();
            check.Equal(ArenaPrompt.ResumePrompt, resumed[resumed.IndexOf("-p") + 1], "a resumed session gets the plain resume prompt");
        }

        var at = result.Position;
        check.That(at is not null, "the result holds the position");
        if (at is not null)
        {
            check.That(at is { Turn: 3, Run: 2, Reshuffle: true, Mode: "reshuffled" } && at.DealSeed == seed, "the turn, the run and its deal");
            check.Equal(start.StateHash, at.StateHash, "the position's state hash");
            check.Equal(start.Hp, at.HpAtStart, "the HP it started at");
            check.Equal(start.Actions.Count, at.ReplayedActions, "the recorded actions applied");
            check.Equal(start.SnapshotsChecked, at.SnapshotsChecked, "the recorded snapshots compared");
            check.That(at is { SourceFolder: "/runs/pilot", SourceModel: "claude-opus-5-5", SourceOutcome: "won", SourceHpLeft: 11 }, "where the position came from");
            check.Sequence(played.Lines[3], at.SourceLine, "what the recorded session did there");
            check.That(at.Line.Count == 0 && at.LineIds.Count == 0 && !at.LineComplete, "a session that made no call left no line");
        }
        check.Equal(("boss-878005", 3, start.StateHash), result.PositionKey ?? ("", 0, ""), "what makes it a run of this position");
        check.Equal("new", result.Experiment, "the label");
        check.Equal("0123456789ab", result.PromptHash, "the prompt hash is recorded as for any fight");
        // No session made a call, on a fight that was already on turn 3 with cards played: still "not played", so it is played again.
        check.That(start.CardsPlayed > 0, "cards had been played before the position");
        check.Equal(Outcomes.Error, result.Outcome, "a run no session made a call in wasn't played");
        check.That(result.Error is not null && result.Error.Contains("without one call to the fight's tools", StringComparison.Ordinal), "and says so: " + result.Error);
        check.Equal(3, result.Turns, "the fight stood where it started");

        // A whole fight is untouched by all this.
        var whole = new ClaudeFight
        {
            Scenario = start.Scenario, Model = "claude-x", Effort = "medium",
            ClaudePath = "/nowhere/claude", SystemPrompt = ArenaPrompt.Build(repo).Text, Repo = repo,
            Environment = new Dictionary<string, string?>(), Launcher = new NoLauncher(),
        };
        check.That(whole.NewGame() is { Turn: 1, CardsPlayed: 0 }, "a fight without a position starts at its first turn");
        var wholeResult = whole.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        check.That(wholeResult.Position is null && wholeResult.PositionKey is null && wholeResult.ToJson()["position"] is null, "and its result and line hold no position");
    }

    // ---------- the result line ----------

    // A position run's line: on a scenario no build can generate, with HP and line chosen by hand.
    private static FightResult Ran(string scenario, int turn, string label, int run, int? hp, string[] line, bool reshuffle = false, string state = "aaaaaaaaaaaa",
        string model = "claude-x", string effort = "medium", string? outcome = null, string? prompt = "111111111111", bool complete = true) => new()
    {
        ScenarioId = scenario, Tier = scenario.Split('-')[0], Summary = $"a fight, vs Something ({scenario})", Model = model, Effort = effort, Experiment = label,
        Outcome = outcome ?? (hp is null ? Outcomes.Lost : Outcomes.Won), HpLeft = hp ?? 0, MaxHp = 80, Turns = turn + 3, CardsPlayed = 20, PromptHash = prompt, WikiHash = "cccccccccccc",
        Position = new PositionInfo
        {
            Turn = turn, Run = run, Reshuffle = reshuffle, DealSeed = reshuffle ? PositionStart.DealSeed(scenario, turn, run) : null, StateHash = state, HpAtStart = 41,
            ReplayedActions = 17, SnapshotsChecked = 5, SourceFolder = "/runs/pilot", SourceModel = "claude-opus-5-5", SourceOutcome = Outcomes.Won, SourceHpLeft = 11,
            SourceLine = ["Play Bash on Kin Priest", "Play Strike on Kin Priest", "End turn"],
            Line = line, LineIds = line.Select((_, i) => i == line.Length - 1 && complete ? "end_turn" : $"play:{i}").ToArray(), LineComplete = complete,
        },
    };

    private static readonly string[] AllIn = ["Play Bash on Kin Priest", "Play Strike on Kin Priest", "End turn"];
    private static readonly string[] Careful = ["Play Strike on Kin Follower (e1)", "Play Defend", "End turn"];
    private static readonly string[] OilLate = ["Play Breakthrough", "Use Snecko Oil", "Play Stomp+", "End turn"];
    private static readonly string[] OilFirst = ["Use Snecko Oil", "Play Breakthrough", "End turn"];

    private static void PositionLine(Check check)
    {
        var run = Ran("boss-x1", 4, "new", 3, 12, Careful, reshuffle: true);
        var json = run.ToJson();
        var keys = json.Select(pair => pair.Key).ToList();
        check.Equal(keys.IndexOf("refused_actions") + 1, keys.IndexOf("position"), "position stands after the simulator's counters");
        var position = (JsonObject)json["position"]!;
        check.Sequence(["turn", "run", "mode", "deal_seed", "state_hash", "hp_at_start", "replayed_actions", "snapshots_checked", "from", "line", "line_ids", "line_complete"],
            position.Select(pair => pair.Key), "the position object's keys");
        check.Sequence(["results", "model", "outcome", "hp_left", "line"], ((JsonObject)position["from"]!).Select(pair => pair.Key), "and those of where it came from");
        check.Equal("reshuffled", FightResult.Str(position["mode"]), "mode in the line");
        check.Equal("[\"Play Strike on Kin Follower (e1)\",\"Play Defend\",\"End turn\"]", position["line"]!.ToJsonString(new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), "the labels as they ran");
        check.Equal("111111111111", FightResult.Str(json["prompt_hash"]), "a position run's line records the prompt hash");

        var back = FightResult.FromJson((JsonObject)JsonNode.Parse(run.ToLine())!)!;
        check.That(back.Position is not null, "the position reads back");
        if (back.Position is { } read)
        {
            var written = run.Position!;
            check.That(read.Turn == 4 && read.Run == 3 && read.Reshuffle && read.DealSeed == written.DealSeed && read.StateHash == "aaaaaaaaaaaa" && read.HpAtStart == 41
                       && read.ReplayedActions == 17 && read.SnapshotsChecked == 5 && read.LineComplete, "its numbers read back");
            check.That(read is { SourceFolder: "/runs/pilot", SourceModel: "claude-opus-5-5", SourceOutcome: "won", SourceHpLeft: 11 }, "where it came from reads back");
            check.Sequence(written.SourceLine, read.SourceLine, "the recorded line reads back");
            check.Sequence(written.Line, read.Line, "the run's line reads back");
            check.Sequence(written.LineIds, read.LineIds, "and its ids");
        }
        check.Equal(("boss-x1", 4, "aaaaaaaaaaaa"), back.PositionKey ?? ("", 0, ""), "the position's key read back");

        var onRecorded = FightResult.FromJson((JsonObject)JsonNode.Parse(Ran("boss-x1", 4, "old", 1, null, AllIn).ToLine())!)!.Position!;
        check.That(onRecorded is { Reshuffle: false, DealSeed: null, Mode: "recorded" }, "a run on the recorded deal has no deal seed");
        check.That(((JsonObject)Ran("boss-x1", 4, "old", 1, null, AllIn).ToJson()["position"]!)["deal_seed"] is null, "nor does its line");

        // A whole fight's line is what it was: no position, and no key of it.
        var whole = Fought("boss-x1", "boss", "base", "medium", 20);
        check.That(whole.Position is null && whole.ToJson()["position"] is null, "a whole fight's line has no position");
        check.That(FightResult.FromJson((JsonObject)JsonNode.Parse(whole.ToLine())!)!.Position is null, "and reads back without one");
    }

    private static void PositionSkips(Check check)
    {
        var played = Record("boss-878005");
        var start = PositionReplay.To(played.Recording, 3);
        FightResult Line(int run, string label = "base", string effort = "medium", bool reshuffle = false, string? outcome = null, string model = "claude-x", string? state = null) =>
            Ran("boss-878005", 3, label, run, 10, Careful, reshuffle, state ?? start.StateHash, model, effort, outcome);
        bool Skips(string[] switches, int run, IReadOnlyList<FightResult> lines, string model = "claude-x") =>
            PositionCommand.AlreadyPlayed(lines, start, ArenaOptions.Parse(["position", "a", "boss-878005", "claude-x", "--turn", "3", .. switches]))(model, run);

        FightResult[] earlier = [Line(1), Line(2), Line(3, outcome: Outcomes.Error), Line(4, outcome: Outcomes.Timeout), Line(5, outcome: Outcomes.Abandoned)];
        check.That(Skips([], 1, earlier) && Skips([], 2, earlier) && Skips([], 5, earlier), "the same command again skips the runs it has (won, lost or abandoned)");
        check.That(!Skips([], 3, earlier) && !Skips([], 4, earlier), "a run that ended as an error or a timeout is played again");
        check.That(!Skips([], 6, earlier), "a run number it doesn't have is played");
        check.That(!Skips([], 1, earlier, "claude-y"), "another model's run isn't skipped");
        check.That(!Skips(["--label", "new"], 1, earlier) && !Skips(["--forecast"], 1, earlier) && !Skips(["--brief"], 1, earlier), "another label plays every run of its own");
        check.That(!Skips(["--effort", "high"], 1, earlier), "so does another effort");
        check.That(!Skips(["--reshuffle"], 1, earlier), "and the reshuffled deal: run 1 on the recorded deal is not run 1 reshuffled");
        check.That(Skips(["--reshuffle"], 1, [Line(1, reshuffle: true)]) && !Skips([], 1, [Line(1, reshuffle: true)]), "nor the other way round");
        check.That(!Skips([], 1, [Line(1, state: "ffffffffffff")]), "the same turn rebuilt from another recording (another state) is another position");
        check.That(Skips(["--label", "new", "--forecast"], 1, [Line(1, label: "new+forecast")]), "a named batch with a switch skips its own");

        // A whole fight and a position run of the same scenario never stand in for each other.
        var scenario = start.Scenario;
        var wholeFight = Played(scenario, "medium", "base");
        check.That(!Skips([], 1, [wholeFight]), "a whole fight of the scenario is no run of the position");
        var run = ArenaOptions.Parse(["run", "--models", "claude-x"]);
        check.That(!RunCommand.AlreadyPlayed([Line(1)], run)(scenario, "claude-x"), "and a position run is no fight of the scenario: the batch still plays it");
        check.That(RunCommand.AlreadyPlayed([Line(1), wholeFight], run)(scenario, "claude-x"), "which it skips once the whole fight is there");

        check.Equal("boss-878005__claude-opus-5-5__turn4-recorded2.jsonl", PositionCommand.TranscriptName("boss-878005", "claude-opus-5-5", 4, false, 2, "base"), "the transcript of run 2 on the recorded deal");
        check.Equal("boss-878005__claude-opus-5-5__turn4-reshuffled2__new_forecast.jsonl", PositionCommand.TranscriptName("boss-878005", "claude-opus-5-5", 4, true, 2, "new+forecast"),
            "of a reshuffled run under a label");
        check.That(PositionCommand.TranscriptName("boss-878005", "claude-opus-5-5", 1, false, 1, "base") != RunCommand.TranscriptName("boss-878005", "claude-opus-5-5", "base"),
            "never the name of the whole fight's transcript, which a position test reads");
        check.Equal("boss-878005 T4", PositionCommand.Name("boss-878005", 4), "a position's name");
    }

    // ---------- the report and compare ----------

    // Two positions, five runs each, once under the label old (prompt 1111...) and once under new (2222...).
    // HP left by run (L is a loss), and the line taken: A the all-in, C the careful one; l Snecko Oil late, f first.
    //                       run 1   run 2   run 3   run 4   run 5    won   mean HP left
    //   boss-x1 T4   old    L  A    L  A    4  C    L  A    L  A     1/5   4/5 = 0.8
    //                new    11 C    9  C    L  A    12 C    8  C     4/5   40/5 = 8.0
    //   elite-x2 T2  old    30 l    7  f    7  f    L  f    10 f     4/5   54/5 = 10.8
    //                new    30 l    28 l    7  f    30 l    31 l     5/5   126/5 = 25.2
    private static FightResult[] TenRuns(string label, bool reshuffle = false, string? prompt = null, string bossState = "aaaaaaaaaaaa")
    {
        var old = label == "old";
        int?[] boss = old ? [null, null, 4, null, null] : [11, 9, null, 12, 8];
        int?[] elite = old ? [30, 7, 7, null, 10] : [30, 28, 7, 30, 31];
        string[][] bossLines = old ? [AllIn, AllIn, Careful, AllIn, AllIn] : [Careful, Careful, AllIn, Careful, Careful];
        string[][] eliteLines = old ? [OilLate, OilFirst, OilFirst, OilFirst, OilFirst] : [OilLate, OilLate, OilFirst, OilLate, OilLate];
        prompt ??= old ? "111111111111" : "222222222222";
        return
        [
            .. Enumerable.Range(0, 5).Select(i => Ran("boss-x1", 4, label, i + 1, boss[i], bossLines[i], reshuffle, bossState, prompt: prompt)),
            .. Enumerable.Range(0, 5).Select(i => Ran("elite-x2", 2, label, i + 1, elite[i], eliteLines[i], reshuffle, "eeeeeeeeeeee", prompt: prompt)),
        ];
    }

    // new against old: boss +60 points (80% - 20%) and +7.2 HP; elite +20 points and +14.4 HP; over both
    // positions 9/10 against 5/10, mean HP (8.0 + 25.2)/2 = 16.6 against (0.8 + 10.8)/2 = 5.8, +40 points, +10.8 HP.
    private static readonly string[][] NewAgainstOld =
    [
        ["boss-x1 T4", "1/5", "0.8", "4/5", "8.0", "+60 points", "+7.2 HP"],
        ["elite-x2 T2", "4/5", "10.8", "5/5", "25.2", "+20 points", "+14.4 HP"],
        ["all 2 positions", "5/10", "5.8", "9/10", "16.6", "+40 points", "+10.8 HP"],
    ];

    private static void PairRows(Check check, List<string> lines, string where)
    {
        foreach (var expected in NewAgainstOld)
        {
            var row = lines.Select(line => Columns().Split(line.Trim())).FirstOrDefault(cells => cells[0] == expected[0] && cells.Length == expected.Length);
            check.Sequence(expected, row ?? [], $"{where}: the row of {expected[0]}");
        }
    }

    private static void PositionTables(Check check)
    {
        var old = TenRuns("old");
        var @new = TenRuns("new");
        FightResult[] both = [.. old, .. @new];

        // The numbers themselves.
        var sheet = new PositionReport.Sheet(both);
        check.Sequence([new PositionReport.Key("boss-x1", 4, "aaaaaaaaaaaa"), new PositionReport.Key("elite-x2", 2, "eeeeeeeeeeee")], sheet.Positions, "the positions, in the file's order");
        check.Sequence([new PositionReport.Variant("claude-x", "old", "medium", false), new PositionReport.Variant("claude-x", "new", "medium", false)], sheet.Variants,
            "the rows: the label that stands first in the file first");
        var (boss, elite) = (sheet.Positions[0], sheet.Positions[1]);
        var (oldRow, newRow) = (sheet.Variants[0], sheet.Variants[1]);
        check.That(sheet.Of(boss, oldRow) is { Wins: 1, MeanHp: 0.8, WinShare: 0.2 } cell && cell.Counted.Count == 5, "old on the boss position: 1 of 5 won, mean HP left 4/5");
        check.That(sheet.Of(boss, newRow) is { Wins: 4, MeanHp: 8.0, WinShare: 0.8 }, "new on it: 4 of 5, mean 40/5");
        check.That(sheet.Of(elite, oldRow) is { Wins: 4, MeanHp: 10.8 } && sheet.Of(elite, newRow) is { Wins: 5, MeanHp: 25.2 }, "the elite position: 54/5 and 126/5");
        check.Sequence([("Bash on Kin Priest, Strike on Kin Priest", 4, 0, 0.0), ("Strike on Kin Follower (e1), Defend", 1, 1, 4.0)], sheet.Of(boss, oldRow)!.Lines(),
            "old's lines on the boss position: the all-in four times, none won; the careful line once, won with 4");
        check.Sequence([("Strike on Kin Follower (e1), Defend", 4, 4, 10.0), ("Bash on Kin Priest, Strike on Kin Priest", 1, 0, 0.0)], sheet.Of(boss, newRow)!.Lines(),
            "new's: the careful line four times, all won, mean (11 + 9 + 12 + 8)/4 = 10");
        var pair = PositionReport.PairRow.Of("boss-x1 T4", sheet.Of(boss, newRow)!, sheet.Of(boss, oldRow)!);
        check.That(Math.Abs(pair.SharePoints - 60) < 1e-9 && Math.Abs(pair.HpDifference - 7.2) < 1e-9, "new against old on the boss position: +60 points of win share, +7.2 HP");

        // The report.
        var output = new StringWriter();
        PositionReport.Print(both, output, "results.jsonl");
        var lines = output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        check.Equal("Position tests: 20 runs on 2 positions by 1 model (results.jsonl).", lines[0], "the header");
        check.That(lines.Contains(PositionCommand.ModeLine(false)) && !lines.Contains(PositionCommand.ModeLine(true)), "it says which deal the runs went on with: the recorded one");
        check.That(lines.Contains("System prompt by label (its hash): old 111111111111; new 222222222222."), "and the prompt each label was played under");
        var table = lines.Select(line => Columns().Split(line.Trim())).ToList();
        check.Sequence(["position", "model", "label", "effort", "deal", "runs", "won", "win rate (95%)", "HP left", "turns", "min/run", "$/run"],
            table.FirstOrDefault(cells => cells[0] == "position" && cells.Length > 8) ?? [], "the table's columns");
        // 1 of 5: the Wilson interval is 4% to 62%.
        check.Sequence(["boss-x1 T4", "claude-x", "old", "medium", "recorded", "5", "1", "20% (4-62%)", "0.8"], table.FirstOrDefault(cells => cells[0] == "boss-x1 T4" && cells.Length > 8)?.Take(9) ?? [], "old's row on the boss position");
        check.Sequence(["claude-x", "new", "medium", "recorded", "5", "4", "80% (38-96%)", "8.0"], table.FirstOrDefault(cells => cells[0] == "claude-x" && cells.Length > 7 && cells[1] == "new" && cells[7] == "8.0")?.Take(8) ?? [],
            "new's row under it, the position not named again");
        check.Sequence(["elite-x2 T2", "claude-x", "old", "medium", "recorded", "5", "4", "80% (38-96%)", "10.8"], table.FirstOrDefault(cells => cells[0] == "elite-x2 T2" && cells.Length > 8)?.Take(9) ?? [], "old's row on the elite position");
        check.That(lines.Contains("  boss-x1 T4: 41 HP at the start. The recorded session (claude-opus-5-5) played: Bash on Kin Priest, Strike on Kin Priest and won with 11 HP left."),
            "the lines' part names the position, its HP and what the recorded session did: " + lines.FirstOrDefault(line => line.StartsWith("  boss-x1 T4:", StringComparison.Ordinal)));
        var at = lines.IndexOf("    claude-x [old]");
        check.That(at > 0 && lines[at + 1] == "      4 runs: Bash on Kin Priest, Strike on Kin Priest  ->  0 won, HP left 0.0"
                          && lines[at + 2] == "      1 run: Strike on Kin Follower (e1), Defend  ->  1 won, HP left 4.0", "old's two lines on it, the one taken most first");
        check.That(lines.Contains("      4 runs: Breakthrough, Use Snecko Oil, Stomp+  ->  4 won, HP left 29.8"), "new's Snecko Oil line on the elite: (30 + 28 + 30 + 31)/4 = 29.75");
        check.That(lines.Contains("claude-x [new] against claude-x [old], position by position:"), "the later label is set against the earlier one");
        PairRows(check, lines, "by label");
        // Ahead in both positions: 2 against 0 is 2 x 1/4.
        check.That(lines.Any(line => line.StartsWith("  By mean HP left, claude-x [new] was ahead in 2 of 2 positions, level in 0, behind in 0 (sign test p 0.50).", StringComparison.Ordinal)), "and counted position by position");
        check.That(!lines.Any(line => line.Contains("Deal by deal", StringComparison.Ordinal)), "on the recorded deal the runs don't pair one to one");

        // Reshuffled on both sides, run k had the same deal: boss 11:0 9:0 0:4 12:0 8:0, elite 30:30 28:7 7:7 30:0 31:10.
        // More HP in 7 deals, the same in 2, less in 1; 7 against 1 is 2 x (1 + 8)/256 = 0.07.
        var dealt = new StringWriter();
        PositionReport.Print([.. TenRuns("old", reshuffle: true), .. TenRuns("new", reshuffle: true)], dealt);
        check.That(dealt.ToString().Contains(PositionCommand.ModeLine(true), StringComparison.Ordinal) && !dealt.ToString().Contains(PositionCommand.ModeLine(false), StringComparison.Ordinal), "a reshuffled batch says so");
        check.That(dealt.ToString().Contains("on 10 deals both played to the end, claude-x [new] had more HP left in 7, the same in 2, less in 1 (sign test p 0.07); it won 9, claude-x [old] 5.", StringComparison.Ordinal),
            "and its runs are compared deal by deal: " + dealt.ToString().Split('\n').FirstOrDefault(line => line.Contains("Deal by deal", StringComparison.Ordinal)));

        // The recorded deal and the reshuffled one side by side are two rows, never set against each other.
        var twoDeals = new StringWriter();
        PositionReport.Print([.. old, .. TenRuns("old", reshuffle: true)], twoDeals);
        check.That(twoDeals.ToString().Contains("claude-x [old] (recorded)", StringComparison.Ordinal) && twoDeals.ToString().Contains("claude-x [old] (reshuffled)", StringComparison.Ordinal), "rows that differ in the deal say which is which");
        check.That(!twoDeals.ToString().Contains(" against ", StringComparison.Ordinal), "and are not compared: that would compare the deals");

        // A run played again after an error counts once, as its later line; one still an error counts for nothing.
        FightResult[] again = [Ran("boss-x1", 4, "old", 2, null, [], outcome: Outcomes.Error), .. old, Ran("elite-x2", 2, "old", 6, null, [], state: "eeeeeeeeeeee", outcome: Outcomes.Timeout)];
        var replayed = new StringWriter();
        PositionReport.Print(again, replayed);
        check.Equal("Position tests: 10 runs on 2 positions by 1 model, plus 1 that ran out of time.", replayed.ToString().Split('\n')[0].TrimEnd('\r'), "the header after a run was played again, and one timed out");
        check.That(new PositionReport.Sheet(again).Of(boss, oldRow)!.Runs.Count == 5, "the boss position still has five runs");

        // A session that stopped mid-turn is listed under what it had played, marked.
        var cutShort = new StringWriter();
        PositionReport.Print([Ran("boss-x1", 4, "old", 1, null, ["Play Bash on Kin Priest"], outcome: Outcomes.Abandoned, complete: false)], cutShort);
        check.That(cutShort.ToString().Contains("      1 run: Bash on Kin Priest (the turn wasn't ended)  ->  0 won, HP left 0.0", StringComparison.Ordinal), "an unfinished turn is marked as one");

        // The same turn of the same scenario in two states is two positions, told apart by their hashes.
        var twoStates = new PositionReport.Sheet([.. old, .. TenRuns("new", bossState: "bbbbbbbbbbbb")]);
        check.Sequence(["boss-x1 T4 #aaaaaa", "elite-x2 T2", "boss-x1 T4 #bbbbbb"], twoStates.Positions.Select(twoStates.Name), "two states of one turn are two positions");

        // Whole fights and position runs are two reports: neither's lines are in the other's tables.
        var plain = SixFights("base", BaseHp);
        var whole = new StringWriter();
        Report.Print([.. plain, .. old], whole);
        check.Equal("Arena report: 6 fights on 6 scenarios by 1 model, effort medium, experiment base.", whole.ToString().Split('\n')[0].TrimEnd('\r'), "the fights' report leaves position runs out (and their label)");
        var runsOnly = new StringWriter();
        PositionReport.Print([.. plain, .. old], runsOnly);
        check.Equal("Position tests: 10 runs on 2 positions by 1 model.", runsOnly.ToString().Split('\n')[0].TrimEnd('\r'), "the positions' report leaves whole fights out");
        var none = new StringWriter();
        PositionReport.Print(plain, none, "results.jsonl");
        check.Equal("No position runs in results.jsonl." + Environment.NewLine, none.ToString(), "and says so when there are none");
        check.Equal(null, Report.MixedLabels(plain.Concat(old).Where(r => r.Position is null).ToList()), "a position label is no second experiment of the fights");
    }

    private static void PositionCompare(Check check)
    {
        var old = TenRuns("old");
        var @new = TenRuns("new");

        var output = new StringWriter();
        check.Equal(0, CompareCommand.PrintPositions(old, @new, output, "runs/a", "runs/b"), "compare's exit code for position runs");
        var lines = output.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        check.Equal("Arena compare, position runs: B against A, position by position (scenario and turn). A positive difference means B did better.", lines[0], "what it announces");
        check.That(lines.Contains("  A: runs/a") && lines.Contains("     claude-x: experiment old, effort medium, deal recorded, 10 runs that count on 2 positions, system prompt 111111111111"), "what A is, with its prompt's hash");
        check.That(lines.Contains("  B: runs/b") && lines.Contains("     claude-x: experiment new, effort medium, deal recorded, 10 runs that count on 2 positions, system prompt 222222222222"), "and B");
        check.That(lines.Contains("The two sides were played under different system prompts (A 111111111111; B 222222222222): the play prompt, the strategy notes or the wiki index differ."),
            "it says the prompts differ, which here is the point");
        check.Sequence(["position", "A: won", "HP left", "B: won", "HP left", "win share", "HP left difference"],
            lines.Select(line => Columns().Split(line.Trim())).FirstOrDefault(cells => cells[0] == "position") ?? [], "the table's columns");
        PairRows(check, lines, "compare");
        check.That(lines.Any(line => line.StartsWith("  By mean HP left, B was ahead in 2 of 2 positions, level in 0, behind in 0 (sign test p 0.50).", StringComparison.Ordinal)), "the positions counted");
        var boss = lines.IndexOf("    boss-x1 T4");
        check.That(boss > 0 && lines.Skip(boss + 1).Take(4).SequenceEqual(
        [
            "      A  4 runs: Bash on Kin Priest, Strike on Kin Priest  ->  0 won, HP left 0.0",
            "      A  1 run: Strike on Kin Follower (e1), Defend  ->  1 won, HP left 4.0",
            "      B  4 runs: Strike on Kin Follower (e1), Defend  ->  4 won, HP left 10.0",
            "      B  1 run: Bash on Kin Priest, Strike on Kin Priest  ->  0 won, HP left 0.0",
        ]), "the lines each side took on the boss position: " + string.Join(" | ", lines.Skip(Math.Max(0, boss)).Take(5)));
        check.That(!lines.Any(line => line.StartsWith("Note:", StringComparison.Ordinal) || line.StartsWith("Positions left out", StringComparison.Ordinal)), "nothing to remark on, nothing left out");

        // The other way round every sign turns.
        var turned = new StringWriter();
        CompareCommand.PrintPositions(@new, old, turned);
        check.Sequence(["all 2 positions", "9/10", "16.6", "5/10", "5.8", "-40 points", "-10.8 HP"],
            turned.ToString().Split('\n').Select(line => Columns().Split(line.Trim())).FirstOrDefault(cells => cells[0] == "all 2 positions") ?? [], "old against new");

        // Reshuffled on both sides: the same ten deals.
        var dealt = new StringWriter();
        CompareCommand.PrintPositions(TenRuns("old", reshuffle: true), TenRuns("new", reshuffle: true), dealt);
        check.That(dealt.ToString().Contains("on 10 deals both played to the end, B had more HP left in 7, the same in 2, less in 1 (sign test p 0.07); it won 9, A 5.", StringComparison.Ordinal), "reshuffled runs also pair deal by deal");
        var mixedDeals = new StringWriter();
        CompareCommand.PrintPositions(old, TenRuns("new", reshuffle: true), mixedDeals);
        check.That(mixedDeals.ToString().Contains("Note: claude-x played A on the recorded deal and B on the reshuffled one", StringComparison.Ordinal), "one side reshuffled and one not is remarked on");
        check.That(!mixedDeals.ToString().Contains("Deal by deal", StringComparison.Ordinal), "and not paired by deal");

        // The same prompt on both sides, and the same variant twice.
        var samePrompt = new StringWriter();
        CompareCommand.PrintPositions(old, TenRuns("new", prompt: "111111111111"), samePrompt);
        check.That(samePrompt.ToString().Contains("Note: both sides were played under the same system prompt (111111111111), so a difference below is not the prompt's.", StringComparison.Ordinal), "the same prompt on both sides is said");
        var twice = new StringWriter();
        CompareCommand.PrintPositions(old, old, twice);
        check.That(twice.ToString().Contains("Note: A and B hold claude-x as the same variant (experiment old, effort medium, deal recorded) under the same prompt", StringComparison.Ordinal), "the same variant twice is said to be");

        // The same turn rebuilt from another recording is another position: not paired, and named.
        var otherState = new StringWriter();
        check.Equal(0, CompareCommand.PrintPositions(old, TenRuns("new", bossState: "bbbbbbbbbbbb"), otherState), "another state of one position still compares the rest");
        var others = otherState.ToString();
        check.That(others.Contains("Positions left out: boss-x1 T4 (claude-x: A and B are not the same position, state aaaaaaaaaaaa against bbbbbbbbbbbb", StringComparison.Ordinal),
            "a position in two states is left out and named: " + others.Split('\n').FirstOrDefault(line => line.StartsWith("Positions left out", StringComparison.Ordinal)));
        check.Sequence(["elite-x2 T2", "4/5", "10.8", "5/5", "25.2", "+20 points", "+14.4 HP"],
            others.Split('\n').Select(line => Columns().Split(line.Trim())).FirstOrDefault(cells => cells[0] == "elite-x2 T2" && cells.Length == 7) ?? [], "the elite position is still paired");
        check.That(!others.Split('\n').Any(line => Columns().Split(line.Trim()) is { Length: 7 } cells && cells[0].StartsWith("boss-x1", StringComparison.Ordinal)), "the boss position has no row");
        var onlyOne = new StringWriter();
        CompareCommand.PrintPositions(old, @new.Where(r => r.ScenarioId == "elite-x2").ToList(), onlyOne);
        check.That(onlyOne.ToString().Contains("Positions left out: boss-x1 T4 (claude-x: only A has it).", StringComparison.Ordinal), "a position only one side has is left out and named");

        // Refusals.
        var two = new StringWriter();
        check.Equal(1, CompareCommand.PrintPositions([.. old, .. @new], @new, two, "runs/a", "runs/b"), "a folder with two variants of the model's position runs");
        check.That(two.ToString().StartsWith("A (runs/a) holds position runs of claude-x in 2 variants (experiment old, effort medium, deal recorded; experiment new, effort medium, deal recorded)", StringComparison.Ordinal),
            "and it says which: " + two.ToString().Split('\n')[0]);
        check.Equal(1, CompareCommand.PrintPositions(old, @new.Select(r => r with { Model = "claude-y" }).ToList(), new StringWriter()), "no model in common");
        check.Equal(1, CompareCommand.PrintPositions(old, @new, new StringWriter(), model: "claude-y"), "--model names a model without position runs");
        check.Equal(1, CompareCommand.PrintPositions(old, SixFights("base", BaseHp), new StringWriter()), "whole fights on one side are no position runs");

        // Whole fights are compared as ever, whatever position runs lie beside them.
        var mixed = new StringWriter();
        check.Equal(0, CompareCommand.Print([.. SixFights("base", BaseHp), .. old], [.. SixFights("brief", BriefHp), .. @new], mixed, "runs/a", "runs/b"), "whole fights beside position runs");
        PairTable(check, mixed.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToList(), "claude-x: B vs A", "compare beside position runs");
    }
}
