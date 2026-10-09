using System.Text.Json.Nodes;

namespace SpireMind.Core;

public sealed record ToolSpec(string Name, string Description, JsonObject InputSchema);

public sealed record ToolOutcome(JsonNode Payload, bool IsError = false);

/// <summary>
/// The small tool surface the model plays through. Kept deliberately narrow: read the
/// state, wait for your turn, do one legal action. Everything the model needs to decide
/// is in the snapshot, so there is no per-screen tool zoo to learn.
/// </summary>
public sealed class BotTools
{
    private readonly IGameBridge _game;
    private readonly DecisionWaiter _waiter;
    private readonly SnapshotVersioner _versioner;

    public TimeSpan ActSettleTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(240);

    public BotTools(IGameBridge game, DecisionWaiter waiter, SnapshotVersioner versioner)
    {
        _game = game;
        _waiter = waiter;
        _versioner = versioner;
    }

    public IReadOnlyList<ToolSpec> Specs { get; } =
    [
        new("get_state",
            "Return the current Slay the Spire 2 snapshot: screen, facts (hand, enemies with intents, map, rewards, shop...), " +
            "and the legal actions with their ids. awaiting_decision=false means the game is busy or it's not your turn.",
            Schema(new JsonObject())),
        new("wait_for_decision",
            "Block until it is your turn to decide (or timeout_seconds passes), then return the snapshot. " +
            "Use this instead of polling get_state, e.g. during enemy turns or while a co-op teammate chooses.",
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
    ];

    public async Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "get_state" => new ToolOutcome((await _waiter.CaptureAsync(ct)).ToJson()),
                "wait_for_decision" => await WaitAsync(args, ct),
                "act" => await ActAsync(args, ct),
                _ => new ToolOutcome(Error($"Unknown tool '{name}'"), IsError: true),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolOutcome(Error($"{ex.GetType().Name}: {ex.Message}"), IsError: true);
        }
    }

    private async Task<ToolOutcome> WaitAsync(JsonObject args, CancellationToken ct)
    {
        var seconds = args.TryGetPropertyValue("timeout_seconds", out var t) && t is JsonValue v && v.TryGetValue<int>(out var s)
            ? s : 60;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, (int)MaxWait.TotalSeconds));
        var snap = await _waiter.WaitAsync(afterVersion: null, timeout, ct);
        return new ToolOutcome(snap.ToJson());
    }

    private async Task<ToolOutcome> ActAsync(JsonObject args, CancellationToken ct)
    {
        var actionId = (args["action_id"] as JsonValue)?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(actionId))
            return new ToolOutcome(Error("action_id is required"), IsError: true);

        var before = await _waiter.CaptureAsync(ct);

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
        return new ToolOutcome(new JsonObject { ["done"] = actionId, ["next"] = next.ToJson() });
    }

    private static ToolOutcome Rejected(string why, GameSnapshot current) =>
        new(new JsonObject { ["error"] = why, ["current"] = current.ToJson() }, IsError: true);

    private static JsonObject Error(string message) => new() { ["error"] = message };

    private static JsonObject Schema(JsonObject properties, string[]? required = null)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required is { Length: > 0 })
            schema["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray());
        return schema;
    }
}
