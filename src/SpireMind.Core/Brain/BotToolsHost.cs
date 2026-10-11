using System.Text.Json.Nodes;

namespace SpireMind.Core.Brain;

/// <summary>
/// Hands <see cref="BotTools"/>, the same instance the MCP endpoint uses, to <see cref="ApiBrain"/>.
/// BotTools has the members <see cref="IToolHost"/> asks for but doesn't declare the interface,
/// so this forwards to it. Thread-safe as BotTools is.
/// </summary>
public sealed class BotToolsHost(BotTools tools) : IToolHost
{
    public IReadOnlyList<ToolSpec> Specs => tools.Specs;

    // The brain holds a repeated wait itself (ApiBrain.RepeatWaitHold), so BotTools doesn't.
    public Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct) =>
        tools.CallAsync(name, args, takeChat: true, holdRepeats: false, ct);
}
