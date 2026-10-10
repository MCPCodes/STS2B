using Godot;
using MegaCrit.Sts2.Core.Modding;
using SpireMind.Core;
using SpireMind.Game;
using SpireMind.Patches;

namespace SpireMind;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Version = "0.1.0";

    /// <summary>Override with the SPIREMIND_PORT env var, e.g. a second co-op instance on 17773.</summary>
    public const int DefaultPort = 17772;

    private const string HarmonyId = "mcpcodes.spiremind";

    private static BridgeServer? _server;
    private static ChatHub? _chat;
    private static bool _identityFailed;
    private static bool _overlayFailed;
    private static bool _bubblesFailed;

    internal static MainThreadQueue MainThread { get; } = new() { OnError = ex => LogError($"main thread: {ex}") };

    /// <summary>The chat box, for the input patches; null until Initialize builds it, or if that failed.</summary>
    internal static ChatOverlay? Overlay { get; private set; }

    public static void Initialize()
    {
        try
        {
            // Read-only observation patches (choice prompts and the like) and the chat box's
            // keys. Failing to patch only loses that feature.
            var harmony = new HarmonyLib.Harmony(HarmonyId);
            ApplyPatches(harmony);

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

            Overlay = StartOverlay(chat, ChatKeysPatched());
            tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(OnFrame));
        }
        catch (Exception ex)
        {
            LogError($"failed to start: {ex}");
        }
    }

    /// <summary>
    /// Applies each patch class on its own. Harmony's PatchAll stops at the first class that
    /// fails, so a chat patch whose target moved in a game update would also cost the choice
    /// prompts, or the other way round.
    /// </summary>
    private static void ApplyPatches(HarmonyLib.Harmony harmony)
    {
        IEnumerable<Type> types;
        try { types = HarmonyLib.AccessTools.GetTypesFromAssembly(typeof(ModEntry).Assembly); }
        catch (Exception ex)
        {
            LogError($"harmony patches skipped: {ex.Message}");
            return;
        }

        foreach (var type in types)
        {
            // Classes without patch attributes are no-ops here, as in PatchAll.
            try { harmony.CreateClassProcessor(type).Patch(); }
            catch (Exception ex) { LogError($"harmony: {type.Name} skipped: {ex.Message}"); }
        }
    }

    /// <summary>Whether the chat box gets its keys from the input manager prefix, so it needn't poll the hotkey.</summary>
    private static bool ChatKeysPatched()
    {
        try { return ChatHotkeyPatch.IsApplied(HarmonyId); }
        catch (Exception ex)
        {
            LogError($"chat: checking the hotkey patch failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The in-game chat box. It attaches itself under NGame once the game is ready, from its
    /// Tick. Failing here only loses the overlay; the bridge keeps running.
    /// </summary>
    private static ChatOverlay? StartOverlay(ChatHub chat, bool keysFromPatch)
    {
        try
        {
            var human = ResolveHumanName();
            var hooks = new ChatGameHooks
            {
                Parent = () => NGame.Instance,
                // VERIFY: AddChildSafely is the game's Node extension (docs/chat-and-persona.md) and its namespace is in the global usings.
                AddChild = (parent, child) => parent.AddChildSafely(child),
                Style = () => ChatTheme.Load(LogError),
                BotColor = () => Identity.NameColor,
                UsingController = () => NControllerManager.Instance?.IsUsingController == true,
                ShowBubble = SpeechBubbles.Say,
            };
            var overlay = new ChatOverlay(chat, () => chat.BotName, human, hooks, LogError) { KeysFromPatch = keysFromPatch };
            Log(keysFromPatch
                ? $"chat: press {overlay.Hotkey} to talk as {human}"
                : $"chat: press {overlay.Hotkey} to talk as {human} (polled: the input patch didn't apply, so game hotkeys may fire while typing)");
            return overlay;
        }
        catch (Exception ex)
        {
            LogError($"chat overlay skipped: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Per-frame chat work on the main thread: keep the bot's name and persona current, run the
    /// overlay, advance speech bubbles. Each part fails on its own and is reported once.
    /// </summary>
    private static void OnFrame()
    {
        if (_chat is not { } chat) return;

        try { Identity.Poll(chat); }
        catch (Exception ex) { LogOnce(ref _identityFailed, $"chat identity: {ex}"); }

        try { Overlay?.Tick(); }
        catch (Exception ex) { LogOnce(ref _overlayFailed, $"chat overlay: {ex}"); }

        try { SpeechBubbles.Tick(chat); }
        catch (Exception ex) { LogOnce(ref _bubblesFailed, $"speech bubbles: {ex}"); }
    }

    private static void LogOnce(ref bool logged, string message)
    {
        if (logged) return;
        logged = true;
        LogError(message);
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
