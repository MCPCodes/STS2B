using Godot;
using MegaCrit.Sts2.Core.Modding;
using SpireMind.Core;
using SpireMind.Game;

namespace SpireMind;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Version = "0.1.0";

    /// <summary>Override with the SPIREMIND_PORT env var, e.g. a second co-op instance on 17773.</summary>
    public const int DefaultPort = 17772;

    private static BridgeServer? _server;

    internal static MainThreadQueue MainThread { get; } = new() { OnError = ex => LogError($"main thread: {ex}") };

    public static void Initialize()
    {
        try
        {
            // Read-only observation patches (choice prompts). Failing to patch only loses prompt text.
            try { new HarmonyLib.Harmony("mcpcodes.spiremind").PatchAll(typeof(ModEntry).Assembly); }
            catch (Exception ex) { LogError($"harmony patches skipped: {ex.Message}"); }

            var tree = (SceneTree)Engine.GetMainLoop();
            tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(MainThread.Drain));

            var game = new GameBridge(MainThread);
            var versioner = new SnapshotVersioner();
            var waiter = new DecisionWaiter(game, versioner);
            var tools = new BotTools(game, waiter, versioner);

            _server = new BridgeServer(ResolvePort(), tools, Version, Log);
            _server.Start();
        }
        catch (Exception ex)
        {
            LogError($"failed to start: {ex}");
        }
    }

    private static int ResolvePort() =>
        int.TryParse(System.Environment.GetEnvironmentVariable("SPIREMIND_PORT"), out var p) && p is > 0 and < 65536
            ? p
            : DefaultPort;

    internal static void Log(string message) => GD.Print($"[SpireMind] {message}");

    internal static void LogError(string message) => GD.PrintErr($"[SpireMind] {message}");
}
