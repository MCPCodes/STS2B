using System.Text.Json.Nodes;

namespace SpireMind.Core;

public sealed record ToolSpec(string Name, string Description, JsonObject InputSchema);

public sealed record ToolOutcome(JsonNode Payload, bool IsError = false);

/// <summary>
/// The small tool surface the model plays through. Kept deliberately narrow: read the
/// state, wait for your turn, do one legal action, talk to the human, look at the deck.
/// Everything the model needs to decide is in the snapshot, so there is no per-screen tool
/// zoo to learn. Every result carries a "chat" key when the human has written or paused
/// the bot (see docs/chat.md).
/// </summary>
public sealed class BotTools
{
    private readonly IGameBridge _game;
    private readonly DecisionWaiter _waiter;
    private readonly SnapshotVersioner _versioner;
    private readonly ChatHub _chat;

    /// <summary>The wait_for_decision in progress. A newer one cancels it, so a wait whose
    /// client went away (Esc in Claude Code) can't take chat messages nobody will read.</summary>
    private CancellationTokenSource? _liveWait;

    public TimeSpan ActSettleTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(240);

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
            "Pass the snapshot's version so a stale id is rejected instead of misfiring.",
            Schema(new JsonObject
            {
                ["action_id"] = new JsonObject { ["type"] = "string", ["description"] = "An id from snapshot.actions" },
                ["version"] = new JsonObject { ["type"] = "integer", ["description"] = "snapshot.version the id came from" },
            }, required: ["action_id"])),
        new("say",
            "Say something in the game's chat. This is how you talk to the human playing with you: answer what they " +
            "wrote in chat.messages, or call out a big moment. Speak in character as the character you play " +
            "(chat.you_are), briefly, a sentence or two. Long lines are cut at 280 characters and more than 6 lines " +
            "in 10 seconds are refused.",
            Schema(new JsonObject
            {
                ["text"] = new JsonObject { ["type"] = "string", ["description"] = "What your character says" },
            }, required: ["text"])),
        new("get_deck",
            "Return the full deck with each card's text, plus relics with their text, potions, HP and gold. " +
            "Use it for build decisions (card rewards, shops, deciding Smith vs Rest, events that touch the deck); " +
            "map, reward, shop and rest snapshots only carry deck name counts. Removal and upgrade pickers already list their cards with text.",
            Schema(new JsonObject())),
    ];

    /// <summary>
    /// Run one tool. A cancelled call throws <see cref="OperationCanceledException"/> before
    /// the chat key is built, so its unread messages stay for the next call.
    /// </summary>
    public Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct) =>
        CallAsync(name, args, takeChat: true, ct);

    /// <param name="takeChat">False for the curl debugging routes: unread messages are shown
    /// but stay unread, so peeking at /state doesn't take them from the model.</param>
    internal async Task<ToolOutcome> CallAsync(string name, JsonObject args, bool takeChat, CancellationToken ct)
    {
        ToolOutcome outcome;
        try
        {
            outcome = name switch
            {
                "get_state" => new ToolOutcome(Present(await _waiter.CaptureAsync(ct))),
                "wait_for_decision" => await WaitAsync(args, ct),
                "act" => await ActAsync(args, ct),
                "say" => Say(args),
                "get_deck" => new ToolOutcome(await _game.DescribeDeckAsync(ct)),
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
        return outcome with { Payload = WithChat(outcome.Payload, takeChat) };
    }

    private async Task<ToolOutcome> WaitAsync(JsonObject args, CancellationToken ct)
    {
        var seconds = args.TryGetPropertyValue("timeout_seconds", out var t) && t is JsonValue v && v.TryGetValue<int>(out var s)
            ? s : 60;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, (int)MaxWait.TotalSeconds));

        // Only one wait at a time. The one this replaces throws OperationCanceledException and
        // leaves the messages for this one.
        using var live = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var previous = Interlocked.Exchange(ref _liveWait, live);
        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { /* it finished meanwhile */ }

        try
        {
            // A message for the model ends the wait at once; while paused no decision is handed over.
            var snap = await _waiter.WaitAsync(afterVersion: null, timeout, live.Token,
                interrupt: () => _chat.HasUnread, hold: () => _chat.Paused);
            return new ToolOutcome(Present(snap));
        }
        finally
        {
            Interlocked.CompareExchange(ref _liveWait, null, live);
        }
    }

    private async Task<ToolOutcome> ActAsync(JsonObject args, CancellationToken ct)
    {
        var actionId = (args["action_id"] as JsonValue)?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(actionId))
            return new ToolOutcome(Error("action_id is required"), IsError: true);

        var before = await _waiter.CaptureAsync(ct);

        if (_chat.PausedBy is { } pausedBy)
            return Rejected($"Paused by {pausedBy}. Call wait_for_decision; you can still talk with say.", before);

        if (args["version"] is JsonValue vv && vv.TryGetValue<long>(out var expected) && expected != before.Version)
            return Rejected($"Snapshot changed (you sent version {expected}, current is {before.Version}). Pick from the current actions.", before);

        if (!before.AwaitingDecision)
            return Rejected($"Not your decision right now ({before.Waiting ?? before.Screen}). Call wait_for_decision.", before);

        if (before.Actions.All(a => a.Id != actionId))
            return Rejected($"'{actionId}' is not a legal action now.", before);

        var result = await _game.ExecuteAsync(actionId, ct);
        if (!result.Ok)
            return Rejected(result.Error ?? "The game refused the action.", await _waiter.CaptureAsync(ct));

        var next = await _waiter.WaitAsync(before.Version, ActSettleTimeout, ct);
        var reply = new JsonObject { ["done"] = actionId, ["next"] = Present(next) };
        if (next.Version == before.Version)
            reply["warning"] = "Nothing changed after this action. The game may have ignored it; re-check the state before repeating it.";
        else if (!next.AwaitingDecision)
            reply["warning"] = "The game is still busy. Call wait_for_decision before acting again.";
        return new ToolOutcome(reply);
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
            return snap.ToJson();

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
            chat["messages"] = new JsonArray(messages.Select(m => (JsonNode)new JsonObject
            {
                ["from"] = m.From,
                ["text"] = m.Text,
                ["seconds_ago"] = Math.Max(0, (int)Math.Round((now - m.At).TotalSeconds)),
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
