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
    private static ChatHub? _chat;
    private static ChatOverlay? _overlay;

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

            // One chat shared by the overlay, the tools and the /chat endpoint (docs/chat.md).
            var chat = new ChatHub();
            _chat = chat;

            var game = new GameBridge(MainThread);
            var versioner = new SnapshotVersioner();
            var waiter = new DecisionWaiter(game, versioner);
            var tools = new BotTools(game, waiter, versioner, chat);

            _server = new BridgeServer(ResolvePort(), tools, chat, Version, Log);
            _server.Start();

            _overlay = StartOverlay(tree, chat);
            tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(OnFrame));
        }
        catch (Exception ex)
        {
            LogError($"failed to start: {ex}");
        }
    }

    /// <summary>The in-game chat box. Failing here only loses the overlay; the bridge keeps running.</summary>
    private static ChatOverlay? StartOverlay(SceneTree tree, ChatHub chat)
    {
        try
        {
            var human = ResolveHumanName();
            var overlay = new ChatOverlay(chat, () => chat.BotName, human, showBubble: null, logError: LogError);
            overlay.Attach(tree.Root);
            Log($"chat: press {overlay.Hotkey} to talk as {human}");
            return overlay;
        }
        catch (Exception ex)
        {
            LogError($"chat overlay skipped: {ex}");
            return null;
        }
    }

    /// <summary>Per-frame chat work on the main thread: keep the bot's name current, run the overlay.</summary>
    private static void OnFrame()
    {
        if (_chat is { } chat) Identity.Poll(chat);
        _overlay?.Tick();
    }

    /// <summary>SPIREMIND_HUMAN_NAME, else the OS user name with a capital first letter ("mark" becomes "Mark").</summary>
    private static string ResolveHumanName()
    {
        var name = System.Environment.GetEnvironmentVariable("SPIREMIND_HUMAN_NAME")?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            try { name = System.Environment.UserName.Trim(); }
            catch { name = null; }
            if (!string.IsNullOrEmpty(name)) name = char.ToUpperInvariant(name[0]) + name[1..];
        }
        return string.IsNullOrEmpty(name) ? "Player" : name;
    }

    private static int ResolvePort() =>
        int.TryParse(System.Environment.GetEnvironmentVariable("SPIREMIND_PORT"), out var p) && p is > 0 and < 65536
            ? p
            : DefaultPort;

    internal static void Log(string message) => GD.Print($"[SpireMind] {message}");

    internal static void LogError(string message) => GD.PrintErr($"[SpireMind] {message}");
}
