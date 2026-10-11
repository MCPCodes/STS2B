using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpireMind.Core;

public sealed record ToolSpec(string Name, string Description, JsonObject InputSchema);

public sealed record ToolOutcome(JsonNode Payload, bool IsError = false);

/// <summary>
/// The small tool surface the model plays through. Kept deliberately narrow: read the
/// state, wait for your turn, do one legal action (or a planned run of them), talk to the
/// human, look at the deck, ask who you are. Everything the model needs to decide is in the
/// snapshot, so there is no per-screen tool zoo to learn. Every result carries a "chat" key
/// when the human has written or paused the bot, and results that hand over a decision mark
/// the bot as thinking, for the thought cloud over its character (see docs/chat.md).
/// Calls come from HTTP worker threads and the brain's task, never Godot's main thread, so
/// waiting here (for the game, or for a spoken line to be read) never holds up the game.
/// </summary>
public sealed partial class BotTools
{
    private readonly IGameBridge _game;
    private readonly DecisionWaiter _waiter;
    private readonly SnapshotVersioner _versioner;
    private readonly ChatHub _chat;

    /// <summary>The wait_for_decision in progress. A newer one cancels it, so a wait whose
    /// client went away (Esc in Claude Code) can't take chat messages nobody will read.</summary>
    private CancellationTokenSource? _liveWait;

    /// <summary>The version of the decision the last wait_for_decision handed over, or
    /// <see cref="NoneShown"/>; act and act_sequence clear it.</summary>
    private long _waitShown = NoneShown;
    private const long NoneShown = -1;

    /// <summary>Most ids one act_sequence takes.</summary>
    public const int MaxSequence = 12;

    public TimeSpan ActSettleTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(240);

    /// <summary>How long wait_for_decision holds back a co-op decision the last wait already
    /// handed over, as <see cref="Brain.ApiBrain.RepeatWaitHold"/> does for the in-game brain.</summary>
    public TimeSpan RepeatWaitHold { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How act and act_sequence wait out a spoken line's hold
    /// (<see cref="ChatHub.SpeechHoldLeft"/>); tests replace it.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public BotTools(IGameBridge game, DecisionWaiter waiter, SnapshotVersioner versioner, ChatHub chat)
    {
        _game = game;
        _waiter = waiter;
        _versioner = versioner;
        _chat = chat;
    }

    public IReadOnlyList<ToolSpec> Specs { get; } =
    [
        new("get_state",
            "Return the current Slay the Spire 2 snapshot: screen, facts (hand, enemies with intents, map, rewards, shop...), " +
            "and the legal actions with their ids. awaiting_decision=false means the game is busy or it's not your turn.",
            Schema(new JsonObject())),
        new("wait_for_decision",
            "Block until it is your turn to decide (or timeout_seconds passes), then return the snapshot. " +
            "It also returns early when the human sends you a chat message, maybe with awaiting_decision=false. " +
            "Use this instead of polling get_state, e.g. during enemy turns or while a co-op teammate chooses. " +
            "In co-op, a decision the last wait already returned (your map vote is in and the human's isn't) is held back until " +
            "something changes, for up to a minute, so calling it again really waits for the others. " +
            "While paused, pass the maximum timeout. A newer wait_for_decision cancels one still running.",
            Schema(new JsonObject
            {
                ["timeout_seconds"] = new JsonObject
                {
                    ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 240, ["default"] = 60,
                },
            })),
        new("act",
            "Perform one legal action by id from the latest snapshot, then wait for the game to settle and return the next snapshot. " +
            "version is required: pass the version of the snapshot the id came from, so a stale id is refused instead of misfiring. " +
            "Take the next id and version from the snapshot act returns. In a fight with teammates, an id from an older snapshot " +
            "still runs when only teammates acted since and nothing it rests on moved; otherwise it is refused with what they changed.",
            Schema(new JsonObject
            {
                ["action_id"] = new JsonObject { ["type"] = "string", ["description"] = "An id from snapshot.actions" },
                ["version"] = new JsonObject { ["type"] = "integer", ["description"] = "snapshot.version the id came from" },
            }, required: ["action_id", "version"])),
        new("act_sequence",
            "Play several actions from one snapshot in order, typically a whole combat turn, then return what ran and the snapshot after. " +
            "All ids come from the snapshot named by version; each later one is found again by its label (and copies of a card by their " +
            "cost and text), so shifting hand positions don't matter. A card that asks you to pick cards from your hand (Armaments, True Grit+) " +
            "can be answered in the same sequence: right after it put select:<i> with each card's hand index in this snapshot, then confirm, " +
            "then the rest of the turn. Those picks are checked when the choice opens, and a confirm the game no longer needs is skipped. " +
            "If only one card in hand could be picked, the game takes it without asking: leave the picks out. " +
            "It stops early when a label is gone, the screen changes any other way, your turn ends, " +
            "an action is refused or changes nothing, the game doesn't settle, a chat message arrives or you are paused. " +
            "In a fight with teammates they play at the same time, so before each step it also checks what they changed: your Block, " +
            "energy, HP or powers, a planned target's HP, Block or powers, which enemies are alive, incoming_damage. On such a change it " +
            "stops with \"A teammate acted: ...\" and you plan the rest of the turn from next; keep sequences short while teammates " +
            "haven't ended their turn, and Block last. End a sequence after " +
            "any card or potion that draws, discovers, creates or transforms cards, has a random result you need to see, opens a pick from a " +
            "pile or ends your turn (a card that asks for hand cards and then draws, like Burning Pact: after its picks), and plan the rest " +
            "from the returned snapshot. end_turn can only be the last id; leave it out after a card that ends your turn.",
            Schema(new JsonObject
            {
                ["version"] = new JsonObject { ["type"] = "integer", ["description"] = "snapshot.version the ids came from" },
                ["actions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" },
                    ["minItems"] = 1,
                    ["maxItems"] = MaxSequence,
                    ["description"] = "Ids from snapshot.actions, in the order to play them",
                },
            }, required: ["version", "actions"])),
        new("say",
            "Say something in the game's chat. This is how you talk to the human playing with you: answer what they " +
            "wrote in chat.messages, or call out a big moment. Speak in character as the character you play " +
            "(chat.you_are, get_persona), briefly, a sentence or two. The line also shows as a speech bubble: over your " +
            "character in fights and on the rewards after one, elsewhere beside your figure or your portrait in the top bar. " +
            "A bubble holds about 100 characters, so short lines read best. Long lines are cut at 280 characters " +
            "and more than 6 lines in 10 seconds are refused.",
            Schema(new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string", ["description"] = "What your character says" },
            }, required: ["text"])),
        new("get_deck",
            "Return the full deck with each card's text, plus relics with their text, potions, HP and gold. " +
            "Use it for build decisions (card rewards, shops, deciding Smith vs Rest, events that touch the deck); " +
            "map, reward, shop and rest snapshots only carry deck name counts. Removal and upgrade pickers already list their cards with text.",
            Schema(new JsonObject())),
        new("get_persona",
            "Return who you are as the game describes your character: name, title, pronouns, tone, and some of the " +
            "character's own lines from the game (voice_lines). At character select it is the character just picked. " +
            "Call it once at the start and again after context compaction, then talk in that voice with say: borrow " +
            "the feel of voice_lines, don't quote them back. If it says no character yet, or your character " +
            "(state.player.character, chat.you_are) is not the one it named, call it again once the run has started.",
            Schema(new JsonObject())),
    ];

    /// <summary>
    /// Run one tool. A cancelled call throws <see cref="OperationCanceledException"/> before
    /// the chat key is built, so its unread messages stay for the next call.
    /// </summary>
    public Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct) =>
        CallAsync(name, args, takeChat: true, ct);

    /// <param name="takeChat">False for the curl debugging routes: unread messages are shown
    /// but stay unread, so peeking at /state doesn't take them from the model, and the result
    /// doesn't mark the bot as thinking, since nothing was handed to the model.</param>
    internal Task<ToolOutcome> CallAsync(string name, JsonObject args, bool takeChat, CancellationToken ct) =>
        CallAsync(name, args, takeChat, holdRepeats: true, ct);

    /// <param name="holdRepeats">False for the in-game brain, whose own loop holds a repeated
    /// wait (ApiBrain.RepeatWaitHold), so the two holds don't add up.</param>
    internal async Task<ToolOutcome> CallAsync(string name, JsonObject args, bool takeChat, bool holdRepeats, CancellationToken ct)
    {
        if (name is "act" or "act_sequence")
            Interlocked.Exchange(ref _waitShown, NoneShown);
        ToolOutcome outcome;
        try
        {
            outcome = name switch
            {
                "get_state" => new ToolOutcome(Present(await _waiter.CaptureAsync(ct))),
                "wait_for_decision" => await WaitAsync(args, holdRepeats, ct),
                "act" => await ActAsync(args, ct),
                "act_sequence" => await ActSequenceAsync(args, ct),
                "say" => Say(args),
                "get_deck" => new ToolOutcome(await _game.DescribeDeckAsync(ct)),
                "get_persona" => Persona(),
                _ => new ToolOutcome(Error($"Unknown tool '{name}'"), IsError: true),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            outcome = new ToolOutcome(Error($"{ex.GetType().Name}: {ex.Message}"), IsError: true);
        }
        if (takeChat) TrackThinking(name, outcome.Payload);
        return outcome with { Payload = WithChat(outcome.Payload, takeChat) };
    }

    private async Task<ToolOutcome> WaitAsync(JsonObject args, bool holdRepeats, CancellationToken ct)
    {
        var seconds = args.TryGetPropertyValue("timeout_seconds", out var t) && t is JsonValue v && v.TryGetValue<int>(out var s)
            ? s : 60;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, (int)MaxWait.TotalSeconds));
        var started = DateTime.UtcNow;

        // Only one wait at a time. The one this replaces throws OperationCanceledException and
        // leaves the messages for this one.
        using var live = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previous = Interlocked.Exchange(ref _liveWait, live);
        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { /* it finished meanwhile */ }

        try
        {
            // A message for the model ends the wait at once, and so does /stop, so a wait whose
            // client is gone can't take a later message; while paused no decision is handed over.
            var stops = _chat.Stops;
            bool Interrupt() => _chat.HasUnread || _chat.Stops != stops;
            var snap = await _waiter.WaitAsync(afterVersion: null, timeout, live.Token, Interrupt, hold: () => _chat.Paused);
            if (holdRepeats)
            {
                // A co-op decision the last wait already handed over (the bot voted on the map and
                // the human hasn't yet) is held back until something changes, for up to
                // RepeatWaitHold: the model is waiting on purpose, and each return costs it a turn.
                var left = timeout - (DateTime.UtcNow - started);
                var hold = left < RepeatWaitHold ? left : RepeatWaitHold;
                if (snap.AwaitingDecision && snap.Version == Interlocked.Read(ref _waitShown) && HasTeammates(snap)
                    && hold > TimeSpan.Zero && !Interrupt())
                    snap = await _waiter.WaitAsync(afterVersion: snap.Version, hold, live.Token, Interrupt, hold: () => _chat.Paused);
                Interlocked.Exchange(ref _waitShown, snap.AwaitingDecision && _chat.PausedBy is null ? snap.Version : NoneShown);
            }
            return new ToolOutcome(Present(snap));
        }
        finally
        {
            Interlocked.CompareExchange(ref _liveWait, null, live);
        }
    }

    private async Task<ToolOutcome> ActAsync(JsonObject args, CancellationToken ct)
    {
        // Whatever comes of it, the decision the model was handed is spent; the result marks the next one.
        _chat.MarkThinking(false);

        var actionId = (args["action_id"] as JsonValue)?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(actionId))
            return new ToolOutcome(Error("action_id is required"), IsError: true);

        if (SentVersion(args) is not { } version)
            return new ToolOutcome(Error(VersionProblem(args)), IsError: true);

        // The id as the game offers it in snap, or why it can't run. Sent on an older snapshot
        // of a co-op fight that only teammates moved on (PlanFrom), it is found again by label.
        (string? Id, string? Why) Check(GameSnapshot snap)
        {
            var (plan, why) = PlanFrom(version, snap, [actionId]);
            if (plan is null)
                return (null, why);
            if (!Offers(plan, actionId))
                return (null, $"'{actionId}' is not a legal action now.");
            if (plan == snap)
                return (actionId, null);
            return FindAgain(actionId, plan, snap, new HashSet<int>()) is { } again
                ? (again, null)
                : (null, $"'{LabelOf(plan, actionId)}' is no longer offered. Pick from the current actions.");
        }

        var before = await _waiter.CaptureAsync(ct);
        var (id, why) = Check(before);
        if (id is null)
            return Rejected(why!, before);
        if (await WaitOutSpeechAsync(before, ct))
        {
            // Checked again after the pause, like any stale id.
            before = await _waiter.CaptureAsync(ct);
            (id, why) = Check(before);
            if (id is null)
                return Rejected(why!, before);
        }

        var step = await StepAsync(id, before, ct);
        if (step.Refusal is { } refusal)
            return Rejected(refusal, step.After);

        var reply = new JsonObject { ["done"] = id, ["next"] = Present(step.After) };
        if (Warning(step) is { } warning)
            reply["warning"] = warning;
        return new ToolOutcome(reply);
    }

    /// <summary>
    /// Several actions from one snapshot, in order (docs/brain.md, act_sequence). Every id is
    /// checked against that snapshot before anything runs, except the picks for a hand choice a
    /// card of the sequence opens (<see cref="ForHandChoice"/>), which are checked when it opens.
    /// The first runs as act runs it; each later one is found again by its label
    /// (<see cref="FindAgain"/>), or a pick by the card it names (<see cref="FindPick"/>). It stops
    /// early, keeping what ran, when the game or the human changes the picture (<see cref="WhyStop"/>).
    /// </summary>
    private async Task<ToolOutcome> ActSequenceAsync(JsonObject args, CancellationToken ct)
    {
        _chat.MarkThinking(false);
        var stops = _chat.Stops;

        if (ActionIds(args["actions"]) is not { } ids)
            return new ToolOutcome(Error("actions must be a list of action ids"), IsError: true);
        if (ids.Count is 0 or > MaxSequence)
            return new ToolOutcome(Error($"actions takes 1 to {MaxSequence} ids; you sent {ids.Count}."), IsError: true);
        if (SentVersion(args) is not { } version)
            return new ToolOutcome(Error(VersionProblem(args)), IsError: true);
        if (ids.IndexOf("end_turn") is var endTurn and >= 0 && endTurn < ids.Count - 1)
            return new ToolOutcome(Error("end_turn can only be the last id, since the turn is over once it runs. Nothing was played."), IsError: true);

        // The snapshot the ids came from, or why they can't run on snap. It is snap itself unless
        // the ids came from an older snapshot of a co-op fight that only teammates moved on (PlanFrom).
        (GameSnapshot? Plan, string? Why) Check(GameSnapshot snap)
        {
            var (plan, why) = PlanFrom(version, snap, ids);
            return plan is null ? (null, why) : Illegal(plan, ids) is { } illegal ? (null, illegal) : (plan, null);
        }

        var current = await _waiter.CaptureAsync(ct);
        var (start, why) = Check(current);
        if (start is null)
            return Rejected(why!, current);
        if (await WaitOutSpeechAsync(current, ct))
        {
            // Checked again after the pause, like any stale id.
            current = await _waiter.CaptureAsync(ct);
            (start, why) = Check(current);
            if (start is null)
                return Rejected(why!, current);
        }
        // In a fight with teammates, every step first looks for what they changed (TeammateChanges).
        var coop = IsCoopFight(current);
        OwnStep? opener = null;

        var done = new JsonArray();
        var notNeeded = new JsonArray();
        // Hand indices in start of the cards picked in an upgrade choice, which come back with a "+".
        var upgraded = new HashSet<int>();
        // Hand indices in start of the cards this sequence has played or picked, which a later pick can't mean.
        var used = new HashSet<int>();
        Step? last = null;
        string? stopped = null;
        var k = 0;
        for (; k < ids.Count; k++)
        {
            var id = ids[k];
            var pick = ForHandChoice(start, ids, k);
            if (last is null && current != start)
            {
                // The first id, from an older snapshot: found again like any later one.
                if (FindAgain(id, start, current, upgraded) is not { } first)
                {
                    stopped = $"'{PlannedLabel(start, id)}' is no longer offered.";
                    break;
                }
                id = first;
            }
            if (last is not null)
            {
                stopped = WhyStop(last, start, stops, pick);
                if (stopped is null && coop)
                {
                    // What changed while the last step ran that its card doesn't explain, then
                    // anything at all that changed since it settled.
                    var own = OwnStep.Of(last, opener);
                    if (last.Id.StartsWith("play:", StringComparison.Ordinal))
                        opener = own;
                    var targets = Targets(ids.Skip(k));
                    var changes = TeammateChanges(last.Before, last.After, own, targets);
                    if (changes is null)
                    {
                        current = await FreshAsync(last.After, ct);
                        stopped = Drift(current, start, pick);
                        changes = stopped is null ? TeammateChanges(last.After, current, own: null, targets) : null;
                    }
                    if (changes is not null)
                        stopped = $"A teammate acted: {changes}. Plan the rest of your turn from next.";
                }
                if (stopped is null && pick && current.Screen != HandSelect)
                {
                    // The choice closed by itself once enough was picked: its confirm has nothing left to do.
                    if (id == "confirm" && last.Before.Screen == HandSelect)
                    {
                        notNeeded.Add(id);
                        continue;
                    }
                    stopped = $"'{PlannedLabel(start, id)}' was planned for a choice from your hand, but none is open."
                              + (last.Id.StartsWith("play:", StringComparison.Ordinal) && last.Before.Screen != HandSelect
                                  ? " If only one card could be picked, the game took it without asking." : "");
                }
                if (stopped is null && pick && HandIndex(id) is { } named && used.Contains(named))
                    stopped = $"'{PlannedLabel(start, id)}' names a card this sequence already played or picked, so nothing was picked in its place.";
                var match = stopped is null ? pick ? FindPick(id, start, current) : FindAgain(id, start, current, upgraded) : null;
                if (match is null)
                {
                    stopped ??= $"'{PlannedLabel(start, id)}' is no longer offered.";
                    break;
                }
                if (pick && current.State["selection"]?["mode"] is JsonValue mode && mode.TryGetValue<string>(out var m) && m == "upgrade"
                    && HandIndex(id) is { } picked)
                    upgraded.Add(picked);
                id = match;
            }

            if (HandIndex(ids[k]) is { } at)
                used.Add(at);
            last = await StepAsync(id, current, ct);
            current = last.After;
            if (last.Refusal is { } refusal)
            {
                // It didn't run, so it stays with the ids not run.
                stopped = $"The game refused '{last.Label}': {refusal}";
                break;
            }
            done.Add(new JsonObject { ["id"] = id, ["label"] = last.Label });
        }

        var reply = new JsonObject { ["done"] = done };
        if (notNeeded.Count > 0)
            reply["not_needed"] = notNeeded;
        if (stopped is not null)
        {
            reply["stopped"] = stopped;
            reply["not_run"] = new JsonArray(ids.Skip(k).Select(i => (JsonNode)JsonValue.Create(i)!).ToArray());
        }
        else if (Warning(last!) is { } warning)
        {
            reply["warning"] = warning;
        }
        reply["next"] = Present(current);
        return new ToolOutcome(reply, IsError: done.Count == 0);
    }

    private const string HandSelect = "hand_select";

    /// <summary>
    /// Whether ids[k] answers a hand choice that a card earlier in the sequence opens: a
    /// select:&lt;i&gt; or confirm, not first, in a sequence that starts in combat. Such an id
    /// isn't offered in the snapshot the ids came from; its i is the card's hand index there.
    /// </summary>
    private static bool ForHandChoice(GameSnapshot start, List<string> ids, int k) =>
        k > 0 && start.Screen == "combat" && (ids[k] == "confirm" || ids[k].StartsWith("select:", StringComparison.Ordinal));

    /// <summary>Why the ids can't run on <paramref name="snap"/>, for the up-front check: the first
    /// id the snapshot doesn't offer. A pick for a hand choice must name a card in its hand. Null
    /// when all of them can.</summary>
    private static string? Illegal(GameSnapshot snap, List<string> ids)
    {
        for (var k = 0; k < ids.Count; k++)
        {
            var id = ids[k];
            if (ForHandChoice(snap, ids, k))
            {
                if (id == "confirm" || HandEntry(snap, id, withText: true) is not null)
                    continue;
                return $"'{id}' names no card in your hand, so nothing was played. A pick for a card's choice uses the card's hand index in this snapshot.";
            }
            if (Offers(snap, id))
                continue;
            if (k > 0 && snap.Screen == "combat" && (id.StartsWith("card:", StringComparison.Ordinal) || id.StartsWith("bundle:", StringComparison.Ordinal)))
                return $"'{id}' can't be planned from combat: the cards of that pick aren't in this snapshot. End the sequence at the card that opens it. Nothing was played.";
            return $"'{id}' is not a legal action now, so nothing was played.";
        }
        return null;
    }

    /// <summary>The label of an id from <paramref name="start"/>, or for a hand pick planned there,
    /// the label its choice will show.</summary>
    private static string PlannedLabel(GameSnapshot start, string id) =>
        Offers(start, id) ? LabelOf(start, id)
        : id == "confirm" ? "Confirm selection"
        : HandEntry(start, id, withText: false)?["name"] is JsonValue name && name.TryGetValue<string>(out var n) ? $"Select {n}"
        : id;

    /// <summary>One action run: the snapshot it ran on, the snapshot after, and the game's
    /// refusal when it wouldn't take it (After is then simply the state at that moment).</summary>
    private sealed record Step(string Id, GameSnapshot Before, GameSnapshot After, string? Refusal)
    {
        public string Label => LabelOf(Before, Id);
    }

    /// <summary>Start one action and wait, up to <see cref="ActSettleTimeout"/>, for the game to
    /// settle into a newer decision. Shared by act and every step of act_sequence.</summary>
    private async Task<Step> StepAsync(string actionId, GameSnapshot before, CancellationToken ct)
    {
        // No snapshot from before this action may be acted on once it ran (PlanFrom).
        Interlocked.Exchange(ref _ownActionMark, long.MaxValue);
        try
        {
            var result = await _game.ExecuteAsync(actionId, ct);
            if (!result.Ok)
                return new Step(actionId, before, await _waiter.CaptureAsync(ct), result.Error ?? "The game refused the action.");
            return new Step(actionId, before, await _waiter.WaitAsync(before.Version, ActSettleTimeout, ct), null);
        }
        finally
        {
            Interlocked.Exchange(ref _ownActionMark, _versioner.Current);
        }
    }

    /// <summary>
    /// Waits out what is left of the bot's last spoken line (<see cref="ChatHub.SpeechHoldLeft"/>)
    /// before an action on <paramref name="snap"/>, so the bubble can be read: the model often
    /// calls say and act in one reply, and off combat the action changes the room under the
    /// bubble. In a fight (combat, hand_select) the bubble sits over the character and stays
    /// through the move, so nothing waits. True when it waited, and the caller checks the
    /// game again.
    /// </summary>
    private async Task<bool> WaitOutSpeechAsync(GameSnapshot snap, CancellationToken ct)
    {
        if (snap.Screen is "combat" or "hand_select")
            return false;
        var left = _chat.SpeechHoldLeft();
        if (left <= TimeSpan.Zero)
            return false;
        await Delay(left, ct);
        return true;
    }

    /// <summary>Why no action can run on <paramref name="current"/>: paused, the model's version is
    /// stale, or it isn't the bot's decision. Null when one can.</summary>
    private string? CannotAct(GameSnapshot current, long version)
    {
        if (_chat.PausedBy is { } pausedBy)
            return $"Paused by {pausedBy}. Call wait_for_decision; you can still talk with say.";
        if (version != current.Version)
            return $"Snapshot changed (you sent version {version}, current is {current.Version}). Pick from the current actions.";
        if (!current.AwaitingDecision)
            return $"Not your decision right now ({current.Waiting ?? current.Screen}). Call wait_for_decision.";
        return null;
    }

    /// <summary>What act says about a step that went through but didn't land where expected.</summary>
    private static string? Warning(Step step) =>
        step.After.Version == step.Before.Version
            ? "Nothing changed after this action. The game may have ignored it; re-check the state before repeating it."
            : !step.After.AwaitingDecision
                ? "The game is still busy. Call wait_for_decision before acting again."
                : null;

    /// <summary>Why act_sequence should stop after <paramref name="last"/> instead of running the
    /// next id, or null to go on. <paramref name="start"/> is the snapshot the sequence started on.
    /// The screen may only leave start's for the hand choice a card opened, and only while the
    /// next id is a pick for it (<paramref name="pickNext"/>); the caller checks the other way
    /// round, a pick with no choice open. A card that ends the turn (Void Form) leaves the screen
    /// on combat, and the settle wait runs through the enemy turn, so a new turn number is the
    /// only sign of it.</summary>
    private string? WhyStop(Step last, GameSnapshot start, long stops, bool pickNext)
    {
        var now = last.After;
        // First, whatever the game shows: the human took the game back.
        if (_chat.Stops != stops)
            return "The bot was stopped with /stop.";
        if (now.Version == last.Before.Version)
            return $"Nothing changed after '{last.Label}'. The game may have ignored it; re-check the state before repeating it.";
        if (now.Screen != start.Screen && !(pickNext && now.Screen == HandSelect))
            return $"The screen changed to {now.Screen}.";
        if (!now.AwaitingDecision)
            return $"The game is still busy ({now.Waiting ?? "no decision yet"}). Call wait_for_decision before acting again.";
        if (!SameTurn(start, now))
            return $"Your turn ended after '{last.Label}'.";
        if (_chat.PausedBy is { } pausedBy)
            return $"Paused by {pausedBy}. Call wait_for_decision; you can still talk with say.";
        if (_chat.HasUnread)
            return "A chat message arrived.";
        return null;
    }

    /// <summary>Whether two snapshots are in the same combat turn: state.turn and state.round are
    /// unchanged (both absent off combat).</summary>
    private static bool SameTurn(GameSnapshot a, GameSnapshot b) =>
        JsonNode.DeepEquals(a.State["turn"], b.State["turn"]) && JsonNode.DeepEquals(a.State["round"], b.State["round"]);

    /// <summary>
    /// The action in <paramref name="now"/> that does what <paramref name="id"/> did in
    /// <paramref name="start"/>. An id with no index or target (confirm, end_turn, proceed) stays
    /// the same action while it is offered, even when its label carries a count that moved on
    /// ("Confirm selection (1 chosen)"). Any other id is found by its label: the actions with that
    /// label, else, for an id with a target (">e1"), those at the same target whose label matches
    /// once a twin tag like " (e1)" is dropped from both; the tag comes and goes as a namesake
    /// lives or dies. A card picked in an upgrade choice earlier in the sequence (its start hand
    /// index is in <paramref name="upgraded"/>) is found under its upgraded name only, "Bash+" for
    /// "Bash", at the same target, so a plain copy is never played in its place. <see cref="PickCopy"/> chooses among
    /// several. Null when nothing matches.
    /// </summary>
    private static string? FindAgain(string id, GameSnapshot start, GameSnapshot now, IReadOnlySet<int> upgraded)
    {
        if (!id.Any(char.IsAsciiDigit))
            return Offers(now, id) ? id : null;

        var label = LabelOf(start, id);
        if (HandIndex(id) is { } index && upgraded.Contains(index))
        {
            if (UpgradedLabel(start, id, TwinTag().Replace(label, "")) is not { } plus)
                return null;
            var at = TargetOf(id);
            return now.Actions.FirstOrDefault(a => TargetOf(a.Id) == at && plus.IsMatch(TwinTag().Replace(a.Label, "")))?.Id;
        }
        var matches = now.Actions.Where(a => a.Label == label).ToList();
        if (matches.Count == 0 && TargetOf(id) is { } target)
        {
            var bare = TwinTag().Replace(label, "");
            matches = now.Actions.Where(a => TargetOf(a.Id) == target && TwinTag().Replace(a.Label, "") == bare).ToList();
        }
        return PickCopy(id, start, now, matches);
    }

    /// <summary>
    /// The pick in <paramref name="now"/>, an open hand choice, that a pick planned in
    /// <paramref name="start"/> means. confirm is itself while offered. select:&lt;i&gt; names the
    /// card at hand index i in start: of the selects offered for a card of that name, the one
    /// whose hand entry is the same (<see cref="PickCopy"/>). The played card has left the hand
    /// and picked ones are not offered again, so positions shift; a card that is gone, already
    /// picked or not allowed in this choice gives null, and nothing runs in its place. The caller
    /// first stops a pick of a card the sequence already played or picked, so a copy of it with
    /// the same name is never taken for it.
    /// </summary>
    private static string? FindPick(string id, GameSnapshot start, GameSnapshot now)
    {
        if (id == "confirm")
            return Offers(now, id) ? id : null;
        if (NameOf(HandEntry(start, id, withText: false)) is not { } name)
            return null;
        var matches = now.Actions
            .Where(a => a.Id.StartsWith("select:", StringComparison.Ordinal) && NameOf(HandEntry(now, a.Id, withText: false)) == name)
            .ToList();
        return PickCopy(id, start, now, matches);

        static string? NameOf(JsonNode? entry) =>
            entry?["name"] is JsonValue v && v.TryGetValue<string>(out var n) ? n : null;
    }

    /// <summary>
    /// What the label of play id <paramref name="id"/>, <paramref name="label"/> in
    /// <paramref name="start"/>, reads once its card is upgraded: the card's name with a "+" or
    /// "+N" after it, the rest unchanged ("Play Bash+ on Jaw Worm"). Null when the label doesn't
    /// start with the card's name.
    /// </summary>
    private static Regex? UpgradedLabel(GameSnapshot start, string id, string label)
    {
        if (HandEntry(start, id, withText: false)?["name"] is not JsonValue v || !v.TryGetValue<string>(out var name))
            return null;
        var head = $"Play {name}";
        if (!label.StartsWith(head, StringComparison.Ordinal))
            return null;
        return new Regex($"^{Regex.Escape(head)}\\+\\d*{Regex.Escape(label[head.Length..])}$", RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Of <paramref name="matches"/>, all labelled alike, the one most likely to be the card
    /// <paramref name="id"/> named in <paramref name="start"/>. A label names only the card, so
    /// copies with another cost, enchantment or text look the same: those whose hand entry
    /// equals the original's go first, compared with their text and then without it (a Strength
    /// gain rewrites every attack's text). Of those left, the same id if it is there, else the
    /// first. Null when there are none.
    /// </summary>
    private static string? PickCopy(string id, GameSnapshot start, GameSnapshot now, List<LegalAction> matches)
    {
        if (matches.Count > 1)
        {
            foreach (var withText in new[] { true, false })
            {
                if (HandEntry(start, id, withText) is not { } card)
                    break;
                var copies = matches.Where(a => JsonNode.DeepEquals(HandEntry(now, a.Id, withText), card)).ToList();
                if (copies.Count > 0)
                {
                    matches = copies;
                    break;
                }
            }
        }
        return (matches.FirstOrDefault(a => a.Id == id) ?? matches.FirstOrDefault())?.Id;
    }

    /// <summary>Keys of a hand entry that say where the card sits or what it can do right now,
    /// not which card it is.</summary>
    private static readonly string[] PlaceKeys = ["i", "playable", "unplayable", "vs", "selected", "selectable"];

    /// <summary>The state.hand entry of the card a play or select id names ("play:2>e0" and
    /// "select:2" both name hand card 2) without <see cref="PlaceKeys"/>, and without its text
    /// unless <paramref name="withText"/>. Null for any other id.</summary>
    private static JsonNode? HandEntry(GameSnapshot snap, string id, bool withText)
    {
        if (HandIndex(id) is not { } i || snap.State["hand"] is not JsonArray hand || i >= hand.Count)
            return null;
        var entry = hand[i]?.DeepClone();
        if (entry is JsonObject card)
        {
            foreach (var key in PlaceKeys)
                card.Remove(key);
            if (!withText)
                card.Remove("text");
        }
        return entry;
    }

    /// <summary>The hand index a play or select id names, 2 of "play:2>e0" and of "select:2". Null for any other id.</summary>
    private static int? HandIndex(string id)
    {
        var colon = id.IndexOf(':');
        if (colon < 0 || id[..colon] is not ("play" or "select"))
            return null;
        var end = id.IndexOf('>');
        return int.TryParse(id[(colon + 1)..(end < 0 ? id.Length : end)], NumberStyles.None, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    private static string LabelOf(GameSnapshot snap, string id) => snap.Actions.First(a => a.Id == id).Label;

    private static bool Offers(GameSnapshot snap, string id) => snap.Actions.Any(a => a.Id == id);

    /// <summary>The target part of an id, ">e0" of "play:2>e0", or null when it has none.</summary>
    private static string? TargetOf(string id) => id.LastIndexOf('>') is var at and >= 0 ? id[at..] : null;

    /// <summary>The " (e1)" or " (p0)" the game side adds to a target's name while another
    /// enemy or player shares it.</summary>
    [GeneratedRegex(@" \([ep]\d+\)$")]
    private static partial Regex TwinTag();

    /// <summary>What act and act_sequence say when the call has no version. Without it a second
    /// act sent on the same snapshot would run unchecked on whatever the first one led to.</summary>
    public const string VersionRequired =
        "version is required: pass the version of the snapshot your ids came from, so a stale id is refused instead of misfiring. Nothing was done.";

    /// <summary>Why the call's version can't be used: missing, or sent as something other than an integer.</summary>
    private static string VersionProblem(JsonObject args) =>
        args["version"] is { } sent
            ? $"version must be the integer snapshot.version; you sent {sent.ToJsonString()}. Nothing was done."
            : VersionRequired;

    private static long? SentVersion(JsonObject args) =>
        args["version"] is JsonValue v && v.TryGetValue<long>(out var version) ? version : null;

    private static List<string>? ActionIds(JsonNode? node)
    {
        if (node is not JsonArray array)
            return null;
        var ids = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue v || !v.TryGetValue<string>(out var id))
                return null;
            ids.Add(id);
        }
        return ids;
    }

    private ToolOutcome Say(JsonObject args)
    {
        var text = args["text"] is JsonValue v && v.TryGetValue<string>(out var t) ? t : "";
        try
        {
            return new ToolOutcome(new JsonObject { ["said"] = _chat.Say(text).Text });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new ToolOutcome(Error(ex.Message), IsError: true);
        }
    }

    private ToolOutcome Persona() =>
        _chat.Persona is { } persona
            ? new ToolOutcome(persona)
            : new ToolOutcome(Error("no character yet; call get_persona again once the run has started"), IsError: true);

    private ToolOutcome Rejected(string why, GameSnapshot current) =>
        new(new JsonObject { ["error"] = why, ["current"] = Present(current) }, IsError: true);

    /// <summary>
    /// The snapshot as the model should see it. While paused that is the real state with no
    /// decision and no actions, so the model doesn't plan moves it can't make. The copy keeps
    /// the snapshot's own version; the versioner never sees it.
    /// </summary>
    private JsonObject Present(GameSnapshot snap)
    {
        if (_chat.PausedBy is not { } pausedBy)
        {
            Remember(snap);
            return snap.ToJson();
        }

        return new GameSnapshot
        {
            Version = snap.Version,
            Screen = snap.Screen,
            AwaitingDecision = false,
            Waiting = $"paused by {pausedBy}",
            State = snap.State,
        }.ToJson();
    }

    /// <summary>
    /// Keeps <see cref="ChatHub.ThinkingSince"/> on whether the model holds a decision: set when
    /// the snapshot a result hands back awaits one with actions (act's or act_sequence's next, or
    /// the current one it was refused against), cleared when it doesn't. Paused snapshots never
    /// await one. say, get_deck and get_persona leave it alone, since the model may call them
    /// mid-decision.
    /// </summary>
    private void TrackThinking(string tool, JsonNode payload)
    {
        JsonNode? snapshot;
        switch (tool)
        {
            case "get_state" or "wait_for_decision":
                snapshot = payload;
                break;
            case "act" or "act_sequence":
                snapshot = payload is JsonObject reply ? reply["next"] ?? reply["current"] : null;
                break;
            default:
                return;
        }
        var awaiting = snapshot is JsonObject s
                       && s["awaiting_decision"] is JsonValue v && v.TryGetValue<bool>(out var a) && a
                       && s["actions"] is JsonArray { Count: > 0 };
        _chat.MarkThinking(awaiting);
    }

    /// <summary>
    /// Adds the "chat" key when there are unread messages or the bot is paused. Messages are
    /// taken (marked read) here, right before the result goes back to the model, unless
    /// <paramref name="take"/> is false.
    /// </summary>
    private JsonNode WithChat(JsonNode payload, bool take)
    {
        if (payload is not JsonObject json)
            return payload;

        var messages = take ? _chat.TakeUnread() : _chat.PeekUnread();
        var paused = _chat.Paused;
        if (messages.Count == 0 && !paused)
            return json;

        var chat = new JsonObject();
        if (_chat.BotName is { } name) chat["you_are"] = name;
        if (paused) chat["paused"] = true;
        if (messages.Count > 0)
        {
            var now = DateTime.UtcNow;
            chat["messages"] = new JsonArray(messages.Select(m =>
            {
                var message = new JsonObject
                {
                    ["from"] = m.From,
                    ["text"] = m.Text,
                    ["seconds_ago"] = Math.Max(0, (int)Math.Round((now - m.At).TotalSeconds)),
                };
                // Another bot's line relayed in by the co-op relay, not the human's.
                if (m.Teammate) message["teammate"] = true;
                return (JsonNode)message;
            }).ToArray());
        }
        json["chat"] = chat;
        return json;
    }

    private static JsonObject Error(string message) => new() { ["error"] = message };

    private static JsonObject Schema(JsonObject properties, string[]? required = null)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required is { Length: > 0 })
            schema["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        return schema;
    }
}
