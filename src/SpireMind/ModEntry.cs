using Godot;
using MegaCrit.Sts2.Core.Modding;
using SpireMind.Core;
using SpireMind.Core.Brain;
using SpireMind.Game;
using SpireMind.Patches;
// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using InstanceInfo = SpireMind.Core.Coop.InstanceInfo;

namespace SpireMind;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string Version = "0.1.0";

    /// <summary>Override with the SPIREMIND_PORT env var, e.g. a bot copy for co-op on 17773.</summary>
    public const int DefaultPort = 17772;

    private const string HarmonyId = "mcpcodes.spiremind";

    private static BridgeServer? _server;
    private static ChatHub? _chat;
    private static bool _identityFailed;
    private static bool _overlayFailed;
    private static bool _bubblesFailed;
    private static bool _instancePollFailed;
    private static bool _coopTickFailed;

    /// <summary>What this copy reports about itself to the co-op relay; null if building it failed.</summary>
    private static InstanceInfo? _instance;

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

            // Which copy this is in co-op: Mark's own, or a bot copy started with --sts2bot=1
            // (docs/game-api.md, section g). Each copy has its own port.
            var port = ResolvePort();
            var role = DetectRole(port);
            _instance = NewInstanceInfo(role);

            _server = new BridgeServer(port, tools, chat, Version, Log)
            {
                DebugScreenshot = _ => MainThread.Run(Screenshots.Take),
                Instance = _instance,
            };
            _server.Start();

            // The human copy finds the bot copies on localhost and relays the chat to them, so
            // Mark talks to every bot from his own window. Null for a bot copy, or if it failed.
            var relay = CoopWiring.StartRelay(role, chat, port, MainThread, Log, LogError);

            // The in-game brain and its chat commands (docs/brain.md). Failing here only loses the brain.
            try
            {
                // The game loads mods with LoadFromAssemblyPath, so Location is the DLL's path (empty only for one loaded from bytes).
                var location = typeof(ModEntry).Assembly.Location;
                var server = _server;
                var controller = BrainWiring.Start(chat, tools, waiter, () => game.IsMultiplayer, port,
                    () => server.ExternalClientActive(TimeSpan.FromSeconds(30)),
                    location.Length > 0 ? System.IO.Path.GetDirectoryName(location) : null,
                    role, () => relay?.BotsExist == true, Log, LogError);
                ReportPlaying(_instance, controller);
            }
            catch (Exception ex) { LogError($"brain skipped: {ex}"); }

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
                AddChild = (parent, child) => parent.AddChildSafely(child),
                Style = () => ChatTheme.Load(LogError),
                BotColor = () => Identity.NameColor,
                UsingController = () => NControllerManager.Instance?.IsUsingController == true,
                KeyboardBusy = () => PlatformUtil.IsPlatformOverlayOpen() || NDevConsole.Instance.Visible,
                GameUsesKey = GameUsesKey,
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
    /// True when the game reads this key itself, so the chat must not take it: one of its
    /// hotkeys is bound to it (the player can rebind them; T is free by default), or it is a
    /// key the game reads raw before anything else: F2 for the feedback screen, the keys that
    /// toggle its console and, in co-op, V for the emote wheel, which would send an emote
    /// every time the box opened.
    /// </summary>
    private static bool GameUsesKey(Key key) =>
        key is Key.F2 or Key.Quoteleft or Key.Apostrophe or Key.Asterisk or Key.Asciicircum
        || (key == Key.V && NGame.Instance?.ReactionContainer.InMultiplayer == true)
        || (NInputManager.Instance is { } input
            && NInputManager.remappableKeyboardInputs.Any(name => input.GetShortcutKey(name) == key));

    /// <summary>
    /// Per-frame work on the main thread: keep the bot's name and persona current, run the
    /// overlay, advance speech bubbles, and for co-op keep this copy's NetId and the list of bot
    /// copies the game started current (each at most once a second). Each part fails on its own
    /// and is reported once.
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

        if (_instance is { } instance)
        {
            try { InstancePoll.Poll(instance); }
            catch (Exception ex) { LogOnce(ref _instancePollFailed, $"coop: reading this copy's NetId: {ex}"); }
        }

        try { CoopWiring.Tick(); }
        catch (Exception ex) { LogOnce(ref _coopTickFailed, $"coop: {ex}"); }
    }

    /// <summary>
    /// This copy's role, logged with its warnings. Detect never throws for bad input; if it
    /// throws anyway, the copy counts as the human's with the default folders, which only loses
    /// co-op. Runs on the main thread (Godot's command line).
    /// </summary>
    private static InstanceRole DetectRole(int port)
    {
        try
        {
            var role = InstanceRole.Detect(Godot.OS.GetCmdlineArgs(), port, System.Environment.GetEnvironmentVariable);
            Log($"role: {role}");
            foreach (var warning in role.Warnings)
                LogError($"role: {warning}");
            return role;
        }
        catch (Exception ex)
        {
            LogError($"role: couldn't tell whether this is a bot copy, so it counts as the human's: {ex}");
            return new InstanceRole
            {
                Kind = InstanceKind.Human,
                Port = port,
                ConfigDir = BrainSettings.DefaultConfigDir,
                SharedConfigDir = BrainSettings.DefaultConfigDir,
            };
        }
    }

    /// <summary>What the bridge reports about this copy on /coop/info and /chat, or null if it
    /// couldn't be built (the bridge then reports a human copy).</summary>
    private static InstanceInfo? NewInstanceInfo(InstanceRole role)
    {
        try { return new InstanceInfo(role.IsBot, role.Port, role.InstanceId); }
        catch (Exception ex)
        {
            LogError($"coop: this copy can't report who it is: {ex}");
            return null;
        }
    }

    /// <summary>Lets the bridge say which brain plays in this copy ("api", "subscription" or
    /// "none"), for /bots in the human's window. Read on HTTP worker threads; the controller's
    /// Playing is thread-safe.</summary>
    private static void ReportPlaying(InstanceInfo? instance, BrainController? controller)
    {
        if (instance is null) return;
        instance.Playing = () => controller?.Playing switch
        {
            BrainKind.Api => "api",
            BrainKind.Subscription => "subscription",
            BrainKind.None => "none",
            _ => null,
        };
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
