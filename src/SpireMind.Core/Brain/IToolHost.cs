using System.Text.Json.Nodes;

namespace SpireMind.Core.Brain;

/// <summary>
/// The game tools the brain plays through. <see cref="BotTools"/> has exactly these members;
/// wrap the instance the MCP endpoint uses in <see cref="BotToolsHost"/> to hand it to the
/// brain. Calls come from the brain's background task.
/// </summary>
public interface IToolHost
{
    IReadOnlyList<ToolSpec> Specs { get; }

    /// <summary>Run one tool. A cancelled call throws <see cref="OperationCanceledException"/>;
    /// anything else that goes wrong comes back as an outcome with IsError set.</summary>
    Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct);
}
