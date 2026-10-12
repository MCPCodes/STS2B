using Godot;
using MegaCrit.Sts2.Core.Modding;
using SpireMind.Core;
using SpireMind.Core.Brain;
using SpireMind.Game;
using SpireMind.Patches;
// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using CoopRelay = SpireMind.Core.Coop.CoopRelay;
using InstanceInfo = SpireMind.Core.Coop.InstanceInfo;
using PartyBot = SpireMind.Core.Coop.PartyBot;
// The game side of co-op (src/SpireMind/Game/Coop/), by alias for the same reason.
using CoopGame = SpireMind.Game.Coop.CoopGame;
using GuestMode = SpireMind.Game.Coop.GuestMode;

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
    private static bool _botSelfFailed;

    /// <summary>What this copy reports about itself to the co-op relay; null if building it failed.</summary>
    private static InstanceInfo? _instance;

    internal static MainThreadQueue MainThread { get; } = new() { OnError = ex => LogError($"main thread: {ex}") };

    /// <summary>The chat box, for the input patches; null until Initialize builds it, or if that failed.</summary>
    internal static ChatOverlay? Overlay { get; private set; }

    /// <summary>
    /// A file of this name beside the DLL marks the package made for a guest
    /// (scripts/package-guest.sh puts it there; scripts/install.sh never does). With it the mod
    /// is a guest copy or does nothing at all: the bridge, the brain, the chat box and the bot
    /// launcher can never start on a guest's computer, whatever its launch options say.
    /// </summary>
    internal const string GuestOnlyMarker = "guest-only.txt";

    public static void Initialize()
    {
        // A friend's game that joins the host's lobby from another computer takes its own short
        // path (docs/coop-guest.md): nothing below runs there. Should the check itself fail,
        // the copy starts as it did before guests existed, unless it loaded the guest package:
        // on a guest's computer the mod then does nothing at all.
        var asGuest = false;
        try { asGuest = StartedAsGuest(); }
        catch (Exception ex)
        {
            asGuest = IsGuestPackage();
            LogError(asGuest
                ? $"role: the guest check failed and this is the guest package, so the mod does nothing in this session: {ex.Message}"
                : $"role: the guest check failed, so this copy starts as the host's or a bot's: {ex.Message}");
        }
        if (asGuest) return;

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
            if (role.IsGuest)
            {
                // StartedAsGuest takes every guest copy before this point; should one get here
                // all the same, it must not start the host's machinery.
                LogError("role: this is a guest copy whose own start-up did not run; the mod does nothing more in this session");
                return;
            }
            _instance = NewInstanceInfo(role);
            // A bot copy tells the relay where it stands and takes /bot stop and the lobby
            // nudge through its bridge; the human's copy is left as it is.
            if (_instance is { IsBot: true } botInfo)
                BotSelf.Wire(botInfo, MainThread);
            // A bot copy plans with the party (the sessions run from the human's copy): its tools
            // hold pick screens during a session and offer party_pitch, and its bridge takes the
            // sessions' ops on POST /coop/party. Before the bridge and the brain start.
            if (_instance is { IsBot: true })
                WireParty(tools);

            // The game side of co-op is told which copy this is before the bridge starts. Mark's
            // copy only remembers it. A bot copy puts its window away, takes over its own
            // quitting, starts looking for the host's lobby, and says in its chat when it is
            // stuck (the relay shows that line in Mark's window). It never throws.
            CoopGame.Start(role, ResolveHumanName(), chat);

            _server = new BridgeServer(port, tools, chat, Version, Log)
            {
                DebugScreenshot = _ => MainThread.Run(Screenshots.Take),
                Instance = _instance,
            };
            _server.Start();

            // The co-op relay, started below once the chat box listens; null until then, for a
            // bot copy, or if it failed.
            CoopRelay? relay = null;
            // In a co-op run or lobby. Lock-free flags: the last snapshot's, and the frame
            // tick's look at the lobby and the run.
            bool InCoop() => game.IsMultiplayer || InstancePoll.InCoop;

            // The in-game brain and its chat commands (docs/brain.md). Failing here only loses the brain.
            try
            {
                // The game loads mods with LoadFromAssemblyPath, so Location is the DLL's path (empty only for one loaded from bytes).
                var location = typeof(ModEntry).Assembly.Location;
                var server = _server;
                var controller = BrainWiring.Start(chat, tools, waiter, () => game.IsMultiplayer, port,
                    () => server.ExternalClientActive(TimeSpan.FromSeconds(30)),
                    location.Length > 0 ? System.IO.Path.GetDirectoryName(location) : null,
                    role, InCoop, () => relay?.BotsExist == true, Log, LogError);
                ReportPlaying(_instance, controller);
            }
            catch (Exception ex) { LogError($"brain skipped: {ex}"); }

            Overlay = StartOverlay(chat, ChatKeysPatched());

            // The human copy finds the bot copies on localhost and relays the chat to them, so
            // Mark talks to every bot from his own window. After the chat box, which shows only
            // lines added once it listens, so "Silent joined the chat" is never missed.
            relay = CoopWiring.StartRelay(role, chat, port, MainThread, InCoop, () => InstancePoll.InCoop, CoopNames.NameOf, Log, LogError,
                BrainWiring.NotesRoot, ResolveHumanName(), () => chat.BotName);
            tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(OnFrame));
        }
        catch (Exception ex)
        {
            LogError($"failed to start: {ex}");
        }
    }

    /// <summary>
    /// True when this copy is a guest's and the mod's start-up is done with: a copy started
    /// with --spiremind-host (the role says guest) gets <see cref="GuestMode"/> and nothing
    /// else, and a copy that loaded the guest package without that option gets nothing at all.
    /// False for the host's own copy and for a bot copy, which then start exactly as before.
    /// Never throws. With the guest package, any failure here ends in "nothing at all".
    /// </summary>
    private static bool StartedAsGuest()
    {
        var guestPackage = IsGuestPackage();
        InstanceRole? role = null;
        try
        {
            role = InstanceRole.Detect(Godot.OS.GetCmdlineArgs(), ResolvePort(), System.Environment.GetEnvironmentVariable,
                steamRunning: SteamRunning);
        }
        catch (Exception ex)
        {
            LogError($"role: couldn't tell whether this is a guest copy: {ex}");
        }

        if (role is { IsGuest: true })
        {
            try
            {
                Log($"role: {role}");
                foreach (var warning in role.Warnings)
                    LogError($"role: {warning}");
                if (role.Guest is { } guest)
                    GuestMode.Start(guest, ResolveHumanName());
            }
            catch (Exception ex)
            {
                LogError($"guest: failed to start; the game runs as it would without the mod: {ex}");
            }
            return true;
        }

        if (!guestPackage) return false;
        Log($"role: this is the guest package ({GuestOnlyMarker} is beside the DLL) and the launch options do not ask to join a host " +
            "(--spiremind-host=<address>), so the mod does nothing in this session");
        return true;
    }

    /// <summary>Whether the DLL sits in the package made for a guest. A folder that can't be read counts as "no".</summary>
    private static bool IsGuestPackage()
    {
        try
        {
            // The game loads mods with LoadFromAssemblyPath, so Location is the DLL's path.
            var location = typeof(ModEntry).Assembly.Location;
            return location.Length > 0
                   && System.IO.Path.GetDirectoryName(location) is { Length: > 0 } dir
                   && System.IO.File.Exists(System.IO.Path.Combine(dir, GuestOnlyMarker));
        }
        catch
        {
            return false;
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
    /// copies the game started current (each at most once a second), and in a bot copy drive
    /// its way into the lobby and the run. Each part fails on its own and is reported once.
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

        // A bot copy's snapshot of where it stands, for /bots in the human's window.
        try { BotSelf.Tick(); }
        catch (Exception ex) { LogOnce(ref _botSelfFailed, $"coop: {ex}"); }

        // A bot copy joins the lobby, picks, readies and leaves a finished run by itself. In
        // any other copy this returns at once; it guards itself and never throws.
        CoopGame.Tick();
    }

    /// <summary>
    /// This copy's role, logged with its warnings. Detect never throws for bad input; if it
    /// throws anyway, the copy counts as the human's with the default folders, which only loses
    /// co-op. A copy that runs with Steam is the human's whatever its command line says (a
    /// stray --sts2bot=1 in Steam's launch options); Detect says so in a warning. Runs on the
    /// main thread (Godot's command line, and Steam's flag, settled before mods load).
    /// </summary>
    private static InstanceRole DetectRole(int port)
    {
        try
        {
            var role = InstanceRole.Detect(Godot.OS.GetCmdlineArgs(), port, System.Environment.GetEnvironmentVariable,
                steamRunning: SteamRunning);
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

    /// <summary>Whether this copy runs with Steam. In a method of its own, so a game member that
    /// moved fails inside Detect's guard (which then ignores the bot flag) and not while
    /// DetectRole is compiled.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool SteamRunning() => SteamInitializer.Initialized;

    /// <summary>Gives a bot copy's tools its side of the party's planning sessions. A failure only
    /// loses the sessions in this copy.</summary>
    private static void WireParty(BotTools tools)
    {
        try { tools.Party = new PartyBot(log: Log); }
        catch (Exception ex) { LogError($"coop: this bot copy can't plan with the party: {ex}"); }
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
