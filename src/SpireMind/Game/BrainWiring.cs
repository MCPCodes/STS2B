using SpireMind.Core.Brain;

namespace SpireMind.Game;

/// <summary>
/// Builds the in-game brain at mod load (docs/brain.md): the settings and folders, the spend
/// tracker and transcript, and a <see cref="BrainController"/> that picks between the API brain
/// and the subscription fallback and owns /start, /stop, /budget, /model and /effort. It also
/// hooks game exit, so a Claude Code child never outlives the game.
///
/// Uses only Core, System and Godot types, so it compiles without the game. Everything here
/// is wrapped: a failure only loses the brain, never the bridge or the chat.
/// </summary>
internal static class BrainWiring
{
    /// <summary>How many chat lines a fresh API conversation is offered (it keeps the last 10).</summary>
    private const int RecentChatLines = 20;

    /// <summary>How long a screen check waits for the main thread before it counts as unknown.</summary>
    private static readonly TimeSpan ScreenTimeout = TimeSpan.FromSeconds(10);

    private static int _shutDown;
    private static volatile bool _quiet;

    /// <summary>
    /// Builds the controller, starts it (it starts a brain by itself when autostart is on) and
    /// hooks game exit. Call once on the main thread, after the bridge has started.
    /// </summary>
    /// <param name="chat">The chat the commands and brain lines go to.</param>
    /// <param name="tools">The BotTools instance the MCP endpoint uses; the API brain calls it in-process.</param>
    /// <param name="waiter">Reads the screen for the controller and the subscription brain, without
    /// stamping a snapshot version.</param>
    /// <param name="multiplayer">Whether the run is co-op, which adds strategy/coop.md to the API
    /// brain's prompt. Called on the brain's task, so it must not touch the game.</param>
    /// <param name="port">The bridge's port, whose /mcp endpoint the subscription brain plays through.</param>
    /// <param name="modDir">The folder the mod was loaded from, for the staged prompt and
    /// brain/repo_path; null when unknown.</param>
    /// <param name="log">The mod's log. Called from the brains' tasks too, so it must be thread-safe.</param>
    /// <param name="logError">The mod's error log, for problems with the setup and the files.</param>
    /// <returns>The controller, or null when it couldn't be built.</returns>
    public static BrainController? Start(ChatHub chat, BotTools tools, DecisionWaiter waiter, Func<bool> multiplayer, int port,
        string? modDir, Action<string> log, Action<string> logError)
    {
        // Brain tasks log from the thread pool. Once the game is quitting, their lines are
        // dropped, since the engine they print through may already be gone.
        Action<string> info = line => { if (!_quiet) log(line); };
        Action<string> warn = line => { if (!_quiet) logError(line); };
        try
        {
            var initial = BrainSettings.Load();
            var spend = new SpendTracker(initial.ConfigDir, line => warn($"brain: {line}"));
            var transcript = new Transcript(initial.ConfigDir, warn);
            var paths = new PathLog(modDir, info, warn);
            var clients = new ClientCache(line => info($"brain: {line}"));

            BrainController? controller = null;
            controller = new BrainController(
                loadSettings: () => BrainSettings.Load(),
                saveSettings: settings => settings.Save(),
                createApi: settings =>
                {
                    var folders = paths.Resolve(settings);
                    return new ApiBrain(
                        clients.For(settings.ApiKey!),
                        new BotToolsHost(tools),
                        new NotesTools(folders.NotesRoot),
                        folders.PromptFolder ?? settings.ConfigDir,
                        () => controller!.Settings,
                        spend,
                        character: () => chat.BotName,
                        multiplayer: multiplayer,
                        transcript: transcript,
                        recentChat: () => RecentChat(chat),
                        announce: line => chat.Announce(line),
                        log: info);
                },
                createSubscription: settings =>
                {
                    var folders = paths.Resolve(settings);
                    var options = new SubscriptionBrainOptions
                    {
                        ClaudePath = settings.ClaudePath,
                        WorkingDirectory = folders.SubscriptionDirectory,
                        Port = port,
                        // Read as each session starts, so /model reaches Claude Code at its next one.
                        Model = () => controller!.Settings.Model,
                    };
                    return new SubscriptionBrain(options, ProcessLauncher.Instance,
                        inRun: async ct => RunScreens.InRun(await CurrentScreenAsync(waiter, ct)),
                        announce: line => chat.Announce(line),
                        log: info);
                },
                currentScreen: ct => CurrentScreenAsync(waiter, ct),
                spend: spend,
                chat: chat,
                log: info);

            // The folders as they stand now; each brain start resolves them again, so a repo
            // set in config.json applies at the next /start. Initialize logs the settings (the
            // key only as where it came from) and their warnings.
            paths.Resolve(controller.Settings);
            HookExit(controller, info, warn);
            controller.Initialize();
            return controller;
        }
        catch (Exception ex)
        {
            warn($"brain: couldn't set up, so the bot can't play by itself: {ex}");
            return null;
        }
    }

    /// <summary>The current screen ("combat", "menu"), or null when it can't be read in time.
    /// Awaits the main thread through the waiter, never blocks it. A peek, so the bot's snapshot
    /// version only moves for what its own tools see.</summary>
    private static async Task<string?> CurrentScreenAsync(DecisionWaiter waiter, CancellationToken ct)
    {
        try
        {
            return (await waiter.PeekAsync(ct).WaitAsync(ScreenTimeout, ct)).Screen;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The chat's last lines, oldest first, as "Mark: go for Strength"; the mod's own
    /// lines read "(mod) ...".</summary>
    private static IReadOnlyList<string> RecentChat(ChatHub chat)
    {
        var lines = chat.LinesSince(chat.LastSeq - RecentChatLines);
        var recent = new List<string>(lines.Count);
        foreach (var line in lines)
            recent.Add(line.Kind == ChatLineKind.System ? $"(mod) {line.Text}" : $"{line.From}: {line.Text}");
        return recent;
    }

    /// <summary>
    /// Stops every brain when the game quits: when the scene tree's root leaves the tree (the
    /// engine is still up then) and, in case that never comes, when the process exits. Only
    /// the first one does anything. Shutdown kills a Claude Code child at once.
    /// </summary>
    private static void HookExit(BrainController controller, Action<string> info, Action<string> warn)
    {
        void ShutDown(string why)
        {
            if (Interlocked.Exchange(ref _shutDown, 1) != 0)
                return;
            info($"brain: {why}; stopping the brains");
            try { controller.Shutdown(); }
            catch (Exception ex) { warn($"brain: shutting down failed: {ex}"); }
            _quiet = true;
        }

        try
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                _quiet = true; // the engine may be gone by now, so nothing is printed
                ShutDown("the process is exiting");
            };
        }
        catch (Exception ex)
        {
            warn($"brain: couldn't hook process exit: {ex.Message}");
        }

        try
        {
            if (Engine.GetMainLoop() is SceneTree { Root: { } root })
                root.Connect(Node.SignalName.TreeExiting, Callable.From(() => ShutDown("the game is closing")));
            else
                warn("brain: no scene tree to hook game exit on; relying on process exit");
        }
        catch (Exception ex)
        {
            warn($"brain: couldn't hook game exit: {ex.Message}");
        }
    }

    /// <summary>Resolves the brain's folders for each start, and logs them (with any warning)
    /// when they differ from the last time.</summary>
    private sealed class PathLog(string? modDir, Action<string> info, Action<string> warn)
    {
        private string? _last;

        public BrainPaths Resolve(BrainSettings settings)
        {
            var paths = BrainPaths.Resolve(settings, modDir);
            var text = paths.ToString();
            if (Interlocked.Exchange(ref _last, text) == text)
                return paths;

            info($"brain: {text}");
            foreach (var warning in paths.Warnings)
                warn($"brain: {warning}");
            if (!paths.HasPlayPrompt)
                warn($"brain: no {PromptBuilder.PlayPrompt} found, so the API brain plays without the play prompt. " +
                     $"Set \"repo\" in {settings.ConfigPath} or reinstall the mod with scripts/install.sh.");
            if (paths.SubscriptionDirectory is null)
                info("brain: no repo, so the subscription fallback can't start; the API brain keeps its notes in " + paths.NotesRoot);
            return paths;
        }
    }

    /// <summary>One Messages API client per key, so a /start with the same key reuses the
    /// connection pool. A replaced client isn't disposed, since a stopping brain may still use it.</summary>
    private sealed class ClientCache(Action<string> log)
    {
        private readonly object _gate = new();
        private ClaudeClient? _client;
        private string? _key;

        public IClaudeApi For(string key)
        {
            lock (_gate)
            {
                if (_client is null || _key != key)
                {
                    _client = new ClaudeClient(key, log: log);
                    _key = key;
                }
                return _client;
            }
        }
    }
}
