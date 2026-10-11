using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The rebuilt fight and the recording disagree. A position test never goes on past this: it
/// says where the two differ and starts nothing.
/// </summary>
public sealed class ReplayMismatch(string message) : Exception(message);

/// <summary>
/// The recording has no such position: its fight was over, or its transcript ends, before that
/// turn, or it holds no snapshot of that turn's start to check the rebuilt fight against.
/// Nothing differed on the way; there is just nothing to hand on.
/// </summary>
public sealed class NoSuchPosition(string message) : Exception(message);

/// <summary>
/// A recorded fight rebuilt to the start of one of its player turns and checked against the
/// recording on the way (<see cref="PositionReplay.To"/>): the position a position test hands
/// to new sessions. It holds what it takes to put any number of fresh games there
/// (<see cref="NewGame"/>) and what the recorded session went on to do.
/// </summary>
public sealed record PositionStart
{
    public required Scenario Scenario { get; init; }

    /// <summary>The player turn the position is the start of, from 1.</summary>
    public required int Turn { get; init; }

    /// <summary>The actions that lead there from the scenario's first snapshot: the ids the
    /// recorded session's tools really ran, in order, up to the end of the turn before.</summary>
    public required IReadOnlyList<string> Actions { get; init; }

    /// <summary>The fight's snapshot at the position as JSON text: screen, state and actions as
    /// a session's first call gets them (without the version the tools add). It was compared
    /// with the recording's own snapshot of that moment.</summary>
    public required string Snapshot { get; init; }

    /// <summary>12 hex digits of the SHA-256 of <see cref="Snapshot"/>: two results are of the
    /// same position when scenario, turn and this agree. Another recording of the same scenario
    /// reaches another state on the same turn, and this tells them apart.</summary>
    public string StateHash => ArenaPrompt.Hash(Snapshot);

    public int Hp { get; init; }
    public int MaxHp { get; init; }

    /// <summary>Cards the recorded session had played before the position.</summary>
    public int CardsPlayed { get; init; }

    /// <summary>Recorded snapshots compared with the rebuilt fight on the way.</summary>
    public int SnapshotsChecked { get; init; }

    /// <summary>Of those, the recording's own snapshots of the position itself (at least 1).</summary>
    public int PositionSnapshots { get; init; }

    /// <summary>Recorded action labels (card and target) compared with the rebuilt fight's.</summary>
    public int LabelsChecked { get; init; }

    /// <summary>Recorded snapshots that show the same fight differently than this build does,
    /// in one of the ways <see cref="PositionReplay"/> knows of (the player's name, the layout
    /// of a card choice screen's small picture of the fight). Not a mismatch; each kind is
    /// spelled out in <see cref="Notes"/>.</summary>
    public int DisplayDifferences { get; init; }

    /// <summary>Things worth knowing that are not differences.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>The folder of the run the recording is from, and the model that played it.</summary>
    public string SourceFolder { get; init; } = "";
    public string SourceModel { get; init; } = "";

    /// <summary>How the recorded fight ended, from its line of results.jsonl; null when it has none.</summary>
    public string? SourceOutcome { get; init; }
    public int? SourceHpLeft { get; init; }

    /// <summary>What the recorded session did on this turn: the labels of the actions it ran,
    /// in order, its "End turn" included when it ended the turn.</summary>
    public IReadOnlyList<string> SourceLine { get; init; } = [];

    /// <summary>
    /// A new game of the scenario put at the position, for one run of a position test. A new
    /// fight of the scenario gets the recorded actions applied, one after the other with no busy
    /// spell between them, and must then show the position's snapshot exactly, or nothing is
    /// started. Only then is it put behind a game (and the game behind a bridge), so no session
    /// sees anything of the way there.
    /// <para>
    /// With <paramref name="dealSeed"/> the game gets the engine's own copy of that fight in
    /// which everything the player cannot know is replaced from that seed
    /// (<see cref="Fight.CloneDeterminized"/>): the order of the draw pile and every random
    /// number still to come. What the session sees first is the same snapshot either way, and
    /// that is checked too.
    /// </para>
    /// </summary>
    /// <param name="experiments">The simulator's switches for the fight, as for a whole fight.</param>
    /// <param name="dealSeed">Null continues with the recorded fight's own deal.</param>
    /// <param name="actionDelay">How long the game stays busy after an action; null for a
    /// fight's usual spell. The checks play on without waiting.</param>
    /// <exception cref="ReplayMismatch">The fight did not reach the position.</exception>
    internal ArenaGame NewGame(ExperimentOptions experiments, int? dealSeed, TimeSpan? actionDelay = null)
    {
        var fight = new Fight(FightConfig.From(Scenarios.Setup(Scenario)));
        for (var n = 0; n < Actions.Count; n++)
        {
            var result = fight.Execute(Actions[n]);
            if (!result.Ok)
                throw new ReplayMismatch($"{Scenario.Id} turn {Turn}: action {n + 1} of {Actions.Count} on the way to the position ('{Actions[n]}') was refused " +
                                         $"when it was applied again: {result.Error}");
        }
        if (PositionReplay.Json(fight.Snapshot()) != Snapshot)
            throw new ReplayMismatch($"{Scenario.Id} turn {Turn}: applying the same {Actions.Count} actions again did not reach the same position.\n" +
                                     $"  Checked:  {PositionReplay.Facts(Snapshot)}\n  This time: {PositionReplay.Facts(PositionReplay.Json(fight.Snapshot()))}");
        if (dealSeed is { } seed)
        {
            fight = fight.CloneDeterminized(seed);
            if (PositionReplay.Json(fight.Snapshot()) != Snapshot)
                throw new ReplayMismatch($"{Scenario.Id} turn {Turn}: dealing the unseen cards again (seed {seed}) changed what the player sees, which it must not.\n" +
                                         $"  Before: {PositionReplay.Facts(Snapshot)}\n  After:  {PositionReplay.Facts(PositionReplay.Json(fight.Snapshot()))}");
        }
        return new ArenaGame(Scenario, fight) { Experiments = experiments, ActionDelay = actionDelay ?? ArenaGame.DefaultActionDelay };
    }

    /// <summary>
    /// The seed run <paramref name="deal"/> of a reshuffled position test deals its unseen
    /// cards from. It follows from the scenario, the turn and the run's number alone, so run 3
    /// of a position has the same deal whichever model, prompt or label plays it, and two
    /// batches can be compared deal by deal.
    /// </summary>
    public static int DealSeed(string scenarioId, int turn, int deal)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{scenarioId}|turn {turn}|deal {deal}")));
        return BitConverter.ToInt32(hash, 0) & int.MaxValue;
    }
}

/// <summary>
/// Rebuilds a recorded fight up to the start of a player turn. The scenario's fight is built
/// from its id, the actions the recorded session's tools really ran are applied in the order
/// they ran, and every snapshot the recording holds up to that point is compared with the
/// rebuilt fight. It stops at the first difference with <see cref="ReplayMismatch"/>.
/// <para>
/// What is compared: after every tool result that carries a snapshot (act, act_sequence,
/// wait_for_decision, get_state, refused calls too), the fight's snapshot against the recorded
/// one, whole: screen, awaiting_decision, state and actions, value by value. A snapshot taken
/// while the game was busy holds turn, round, HP and Block, and those are compared. Inside an
/// act_sequence the recording has no snapshot between actions; there each action must be on
/// offer and carry the recorded label (card and target).
/// </para>
/// <para>
/// A recording made by an earlier build shows the same fight a little differently, and two such
/// differences are known, let through and counted. Each is a change to what a snapshot says,
/// not to the fight:
/// <list type="bullet">
/// <item>The player's name. The first recordings name the player <c>Ironclad</c>; since then the
/// snapshots say <c>The Ironclad</c>, as the game does (<see cref="Fight.PlayerName"/>). Only
/// that pair, and only under a <c>me.name</c> key.</item>
/// <item>The small picture of the fight on a card choice screen (<c>card_select</c>,
/// <c>state.combat</c>), whose layout the mod changed (hand cards became objects, the enemies'
/// entries gained keys). There a key that only one side has, and two values of different
/// kinds, are a difference of layout. Two numbers or texts that differ, and lists of different
/// lengths, are a mismatch there as anywhere. The choice itself (its cards, prompt and
/// actions) and every combat snapshot before and after it are compared whole.</item>
/// </list>
/// Anything else that differs is a mismatch. The position's notes say which of the two were
/// met and how often, since a new session then reads that much differently from the recorded one.
/// </para>
/// <para>
/// The position must also have been seen: the recording has to hold at least one full snapshot
/// of the start of that turn, and the rebuilt fight must equal it.
/// </para>
/// </summary>
internal sealed class PositionReplay
{
    private const string CombatScreen = "combat", ChoiceScreen = "card_select", ChoicePicture = "state.combat";

    private readonly Recording _rec;
    private readonly int _turn;
    private readonly Fight _fight;
    private readonly List<string> _actions = [];
    private readonly List<string> _notes = [];
    private int _snapshots, _positionSnapshots, _labels;
    private readonly Display _display = new();

    /// <summary>The player's name in the first recordings, before the snapshots took the game's own.</summary>
    internal const string EarlierPlayerName = "Ironclad";

    /// <summary>What recorded snapshots showed differently from this build in a known way.</summary>
    private sealed class Display
    {
        /// <summary>Snapshots with any such difference.</summary>
        public int Snapshots;

        /// <summary>Snapshots that name the player by the earlier name.</summary>
        public int Names;

        /// <summary>Card choice screens whose small picture of the fight is laid out differently, and one example.</summary>
        public int Pictures;
        public string? PictureExample;

        // What the snapshot being compared holds of each.
        public bool Name;
        public string? Picture;

        public void Next()
        {
            if (Name) Names++;
            if (Picture is not null)
            {
                Pictures++;
                PictureExample ??= Picture;
            }
            if (Name || Picture is not null) Snapshots++;
            Name = false;
            Picture = null;
        }
    }

    private PositionReplay(Recording rec, Scenario scenario, int turn)
    {
        _rec = rec;
        _turn = turn;
        _fight = new Fight(FightConfig.From(Scenarios.Setup(scenario)));
    }

    /// <summary>
    /// The position at the start of player turn <paramref name="turn"/> of the recorded fight.
    /// </summary>
    /// <exception cref="ReplayMismatch">The rebuilt fight differs from the recording.</exception>
    /// <exception cref="NoSuchPosition">The recorded fight has no such turn, or the recording
    /// holds no snapshot of its start.</exception>
    /// <exception cref="ArgumentException">The scenario id cannot be rebuilt by this build.</exception>
    public static PositionStart To(Recording rec, int turn)
    {
        if (turn < 1)
            throw new NoSuchPosition($"There is no turn {turn}: turns count from 1.");
        var scenario = Scenarios.FromId(rec.ScenarioId);
        var replay = new PositionReplay(rec, scenario, turn);
        replay.Play();

        var fight = replay._fight;
        return new PositionStart
        {
            Scenario = scenario,
            Turn = turn,
            Actions = replay._actions,
            Snapshot = Json(fight.Snapshot()),
            Hp = fight.Player.Hp,
            MaxHp = fight.Player.MaxHp,
            CardsPlayed = fight.CardsPlayed,
            SnapshotsChecked = replay._snapshots,
            PositionSnapshots = replay._positionSnapshots,
            LabelsChecked = replay._labels,
            DisplayDifferences = replay._display.Snapshots,
            Notes = replay._notes,
            SourceFolder = rec.Folder,
            SourceModel = rec.Model,
            SourceOutcome = rec.Row?.Outcome,
            SourceHpLeft = rec.Row?.HpLeft,
            SourceLine = replay.RecordedLine(),
        };
    }

    private bool AtPosition => !_fight.IsOver && !_fight.HasPendingChoice && _fight.Turn == _turn;

    private void Play()
    {
        foreach (var call in _rec.Calls)
        {
            if (!call.Answered)
                continue;
            switch (call.Tool)
            {
                case "wait_for_decision" or "get_state":
                    if (call.Snapshot is not null) Compare(call, null);
                    break;
                case "act" or "act_sequence":
                    RanAction? last = null;
                    foreach (var ran in call.Ran)
                    {
                        // The first action the recorded session ran on the turn: the position is what stands now.
                        if (AtPosition)
                        {
                            Reached();
                            return;
                        }
                        Apply(call, ran);
                        last = ran;
                    }
                    if (call.Snapshot is not null) Compare(call, last);
                    break;
            }
        }
        // The recording ends here: on the position, when its session never acted on that turn.
        Reached();
    }

    // What must hold before the position is handed on.
    private void Reached()
    {
        if (!AtPosition)
        {
            var ended = _fight.IsOver
                ? $"the recorded fight was over on turn {_fight.Turn} ({(_fight.Won ? "won" : "lost")})"
                : $"the recording ends on turn {_fight.Turn} with the fight still running";
            throw new NoSuchPosition($"{_rec.ScenarioId} played by {_rec.Model} has no turn {_turn}: {ended}. " +
                                     $"({_actions.Count} recorded actions applied and {_snapshots} snapshots compared on the way, all equal.)");
        }
        if (_positionSnapshots == 0)
            throw new NoSuchPosition($"{_rec.ScenarioId} played by {_rec.Model}: the recording holds no snapshot of the start of turn {_turn} " +
                                     "to check the rebuilt position against, so it is not handed on.");
        if (_display.Names > 0)
            _notes.Add($"the recording names the player {EarlierPlayerName} (in {_display.Names} of the {_snapshots} snapshots compared) where this build's snapshots say " +
                       $"{Fight.PlayerName}, as the game does. Nothing else of those snapshots differs; a new session reads the new name.");
        if (_display.Pictures > 0)
            _notes.Add($"{_display.Pictures} card choice {(_display.Pictures == 1 ? "screen's" : "screens'")} small picture of the fight (state.combat) is laid out differently in the recording " +
                       $"than in this build (for example {_display.PictureExample}); the numbers both layouts hold agree, and the choices themselves were compared whole.");
        if (_rec.UnreadableLines > 0)
            _notes.Add($"{_rec.UnreadableLines} {(_rec.UnreadableLines == 1 ? "line" : "lines")} of the transcript {(_rec.UnreadableLines == 1 ? "isn't" : "aren't")} JSON and {(_rec.UnreadableLines == 1 ? "was" : "were")} skipped.");
    }

    // ---------- one action ----------

    private void Apply(RecordedCall call, RanAction ran)
    {
        if (_fight.IsOver)
        {
            // "done" on the end screen runs and changes nothing.
            if (ran.Id == Fight.DoneAction) return;
            throw new ReplayMismatch($"Transcript line {call.Line}: the recording ran '{ran.Id}', but in the rebuilt fight the fight is already over " +
                                     $"({(_fight.Won ? "won" : "lost")} on turn {_fight.Turn}).");
        }

        var snapshot = _fight.Snapshot();
        var offered = snapshot.Actions.FirstOrDefault(a => a.Id == ran.Id);
        if (offered is null)
            throw new ReplayMismatch(
                $"Transcript line {call.Line}, turn {_fight.Turn}: the recording ran '{ran.Id}'{(ran.Label is null ? "" : $" ({ran.Label})")}, which the rebuilt fight does not offer here.\n" +
                $"  It offers: {string.Join(" ", snapshot.Actions.Select(a => a.Id))}\n  Rebuilt:  {Facts(Reparse(snapshot.ToJson()))}");
        if (ran.Label is not null)
        {
            _labels++;
            if (ran.Label != offered.Label)
                throw new ReplayMismatch($"Transcript line {call.Line}, turn {_fight.Turn}: '{ran.Id}' was '{ran.Label}' in the recording and is '{offered.Label}' in the rebuilt fight.");
        }

        ActionResult result;
        try
        {
            result = _fight.Execute(ran.Id);
        }
        catch (Exception ex)
        {
            throw new ReplayMismatch($"Transcript line {call.Line}, turn {_fight.Turn}: '{ran.Id}' ({offered.Label}) threw in the rebuilt fight: {ex.GetType().Name}: {ex.Message}");
        }
        if (!result.Ok)
            throw new ReplayMismatch($"Transcript line {call.Line}, turn {_fight.Turn}: the recording ran '{ran.Id}' ({offered.Label}); the rebuilt fight refused it: {result.Error}");
        _actions.Add(ran.Id);
    }

    /// <summary>What the recorded session ran on the position's turn, by label, on a copy of the
    /// fight. It stops quietly where the recording stops fitting: the position is what was promised.</summary>
    private List<string> RecordedLine()
    {
        var copy = _fight.Clone();
        var line = new List<string>();
        var skip = _actions.Count;
        foreach (var ran in _rec.Calls.Where(c => c.Answered && c.Tool is "act" or "act_sequence").SelectMany(c => c.Ran))
        {
            if (skip-- > 0) continue;
            if (copy.IsOver || copy.Turn != _turn) break;
            var offered = copy.Snapshot().Actions.FirstOrDefault(a => a.Id == ran.Id);
            if (offered is null || !copy.Execute(ran.Id).Ok) break;
            line.Add(offered.Label);
        }
        return line;
    }

    // ---------- comparing with the recording ----------

    private void Compare(RecordedCall call, RanAction? last)
    {
        var recorded = call.Snapshot!;
        var where = $"Transcript line {call.Line} ({call.Tool}{(last is { } l ? $", after '{l.Id}'{(l.Label is null ? "" : $" {l.Label}")}" : "")}), turn {_fight.Turn} of the rebuilt fight";
        _snapshots++;

        var diffs = new List<string>();
        JsonObject mine;
        var busy = Recording.Str(recorded, "screen") is not null && recorded["awaiting_decision"] is JsonValue av && av.TryGetValue<bool>(out var awaiting) && !awaiting;
        if (busy)
        {
            // A busy snapshot only carries turn, round, hp and block.
            mine = new JsonObject { ["state"] = Reparse(_fight.WaitingState()) };
            Differences(recorded["state"], mine["state"], "state", diffs, choice: false);
        }
        else
        {
            var shown = _fight.Snapshot();
            mine = Reparse(shown.ToJson());
            foreach (var key in new[] { "screen", "awaiting_decision", "state", "actions" })
                Differences(recorded[key], mine[key], key, diffs, choice: shown.Screen == ChoiceScreen);
        }

        if (diffs.Count > 0)
        {
            const int show = 12;
            throw new ReplayMismatch(
                $"{where}: the rebuilt fight differs from the snapshot in the tool result ({diffs.Count} {(diffs.Count == 1 ? "difference" : "differences")}).\n" +
                string.Concat(diffs.Take(show).Select(d => "  " + d + "\n")) + (diffs.Count > show ? $"  ... and {diffs.Count - show} more\n" : "") +
                $"  Recorded: {Facts(recorded)}\n  Rebuilt:  {Facts(mine)}");
        }
        _display.Next();
        if (!busy && AtPosition && Recording.Str(mine, "screen") == CombatScreen)
            _positionSnapshots++;
    }

    /// <summary>A snapshot as the JSON text it travels as.</summary>
    internal static string Json(GameSnapshot snapshot) => snapshot.ToJson().ToJsonString();

    /// <summary>Through text and back, so that both sides of a comparison are JSON read from text.</summary>
    private static JsonObject Reparse(JsonObject json) => (JsonObject)JsonNode.Parse(json.ToJsonString())!;

    /// <summary>
    /// Every place two JSON values differ, as "path: recorded X, rebuilt Y", into
    /// <paramref name="into"/>. The two known differences of display are noted in the replay's
    /// own count instead: the player's earlier name under a <c>me.name</c> key, and, on a card
    /// choice screen (<paramref name="choice"/>) under state.combat, a key only one side has or
    /// two values of different kinds.
    /// </summary>
    private void Differences(JsonNode? recorded, JsonNode? rebuilt, string path, List<string> into, bool choice)
    {
        if (recorded is null && rebuilt is null) return;
        var picture = choice && (path == ChoicePicture || path.StartsWith(ChoicePicture + ".", StringComparison.Ordinal) || path.StartsWith(ChoicePicture + "[", StringComparison.Ordinal));
        void Differs(string what, bool layout)
        {
            if (picture && layout) _display.Picture ??= what;
            else into.Add(what);
        }

        if (recorded is JsonObject ro && rebuilt is JsonObject po)
        {
            foreach (var (key, value) in ro)
            {
                if (!po.ContainsKey(key)) Differs($"{path}.{key}: recorded {Short(value)}, rebuilt has no such key", layout: true);
                else Differences(value, po[key], $"{path}.{key}", into, choice);
            }
            foreach (var (key, value) in po)
                if (!ro.ContainsKey(key)) Differs($"{path}.{key}: recorded has no such key, rebuilt {Short(value)}", layout: true);
            return;
        }
        if (recorded is JsonArray ra && rebuilt is JsonArray pa)
        {
            if (ra.Count != pa.Count) into.Add($"{path}: recorded has {ra.Count} entries, rebuilt {pa.Count}: recorded {Short(ra)}, rebuilt {Short(pa)}");
            for (var i = 0; i < Math.Min(ra.Count, pa.Count); i++) Differences(ra[i], pa[i], $"{path}[{i}]", into, choice);
            return;
        }
        var a = recorded?.ToJsonString() ?? "null";
        var b = rebuilt?.ToJsonString() ?? "null";
        if (a == b) return;
        if (path.EndsWith(".me.name", StringComparison.Ordinal) && FightResult.Str(recorded) == EarlierPlayerName && FightResult.Str(rebuilt) == Fight.PlayerName)
        {
            _display.Name = true;
            return;
        }
        Differs($"{path}: recorded {Short(recorded)}, rebuilt {Short(rebuilt)}", layout: recorded?.GetValueKind() != rebuilt?.GetValueKind());
    }

    private static string Short(JsonNode? node)
    {
        var text = node?.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null";
        return text.Length > 160 ? text[..157] + "..." : text;
    }

    /// <summary>The facts the eye wants first of a snapshot given as JSON text.</summary>
    internal static string Facts(string snapshotJson) => Facts((JsonObject)JsonNode.Parse(snapshotJson)!);

    /// <summary>The facts the eye wants first: HP, Block, energy, hand, each enemy's HP and intents. Works on every screen's snapshot.</summary>
    internal static string Facts(JsonObject snapshot)
    {
        var state = snapshot["state"] as JsonObject ?? snapshot;
        if (Recording.Str(snapshot, "screen") == Fight.OverScreen || state["result"] is not null)
            return Inv($"fight over: {Recording.Str(state, "result")}, turn {state["turn"]}, HP {(state["me"] as JsonObject)?["hp"]}");
        var board = state["combat"] as JsonObject ?? state;
        if (board["me"] is not JsonObject me) return Inv($"turn {state["turn"]}, HP {state["hp"]}, Block {state["block"]} (busy)");
        var hand = board["hand"] is JsonArray h
            ? string.Join(", ", h.Select(c => c is JsonObject card ? Recording.Str(card, "name") : (c as JsonValue)?.ToString()))
            : "";
        var enemies = board["enemies"] is JsonArray es
            ? string.Join("; ", es.OfType<JsonObject>().Select(e => Inv($"{Recording.Str(e, "name")} {e["hp"]}/{e["max_hp"]}{(e["block"] is { } b ? $" Block {b}" : "")} [{string.Join(", ", (e["intents"] as JsonArray ?? []).Select(i => i?.ToString()))}]")))
            : "";
        return Inv($"{Recording.Str(snapshot, "screen") ?? "?"}, turn {board["turn"]}, HP {me["hp"]}, Block {me["block"]?.ToString() ?? "0"}, energy {board["energy"]}, hand [{hand}], enemies: {enemies}");
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
