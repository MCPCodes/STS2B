using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpireMind.Core.Brain;

namespace SpireMind.Game.Coop;

/// <summary>
/// Bot copies as processes. In the human's game (the host) this starts a second copy of the
/// game without Steam for each bot, tracks it and ends it with this game. The other side of
/// the same class runs inside a bot copy: it puts its window away (<see cref="BotWindow"/>),
/// and leaves and quits when asked or when the game that started it is gone.
///
/// A copy is the game's own binary with <c>--force-steam=off --clientId=N --fastmp=join
/// --sts2bot=1</c>, its own profile (<see cref="BotProfile"/>), SPIREMIND_PORT for its bridge
/// and a scrubbed environment. Bots use client ids 1000 to 1002 and ports 17773 to 17775,
/// never 1 and never 17772, which are the host's. The checks are those of scripts/coop-bot.sh
/// as far as they can be made from inside the game; the facts are in
/// sts2-notes/coop-api-v0.107.1/spawn.md.
///
/// <see cref="StartCopyAsync"/>, <see cref="Known"/> and <see cref="ShowLine"/> are the seam
/// the co-op relay uses through Game/CoopBotsAdapter.cs. Which copy this is comes from the
/// mod's <see cref="InstanceRole"/>, handed to <see cref="Start"/> at mod load; without that
/// call nothing bot-only ever runs.
///
/// Nothing here defines a network message or changes game state. Host side: reads only, plus
/// taking the keyboard back if a starting copy took it. Bot side: the game's own leave and
/// quit. Everything public is safe from any thread and never throws. In a solo game nothing
/// runs at all: the host side reads the game when a copy is asked for and hooks it only when
/// one really starts; the bot side runs only in a copy started as a bot.
/// </summary>
internal static class CoopBots
{
    /// <summary>The build the launch flags, the profile fields and the join rules were checked
    /// against. After a game update, check them again (CLAUDE.md), then raise this.</summary>
    public const string CheckedGameVersion = "v0.107.1";

    /// <summary>The first bot's client id (its NetId over ENet and its profile folder default/1000); the next bots count up.</summary>
    public const ulong FirstClientId = 1000;

    /// <summary>The lobby holds four players, and the relay looks for bots on three ports.</summary>
    public const int MaxCopies = 3;

    /// <summary>The first bot's bridge port; the human's game keeps <see cref="ModEntry.DefaultPort"/>.</summary>
    public const int FirstPort = ModEntry.DefaultPort + 1;

    /// <summary>The ENet host is always this NetId, whatever its flags say.</summary>
    public const ulong HostNetId = 1;

    /// <summary>The app's bundle id (Info.plist). Without it in <c>__CFBundleIdentifier</c> the engine forces a new copy's window to the front.</summary>
    private const string BundleId = "com.megacrit.SlayTheSpire2";

    /// <summary>
    /// Redirects the copy's streams to a file and replaces the shell with the game, so the
    /// process id stays the same, no pipe can fill up or break when this game quits, and what
    /// the copy prints before its log file starts is kept. On the way it closes every file
    /// and socket inherited from this game (the lobby's own UDP port must never be held by a copy).
    /// $1 is the console file, the rest is the game and its arguments.
    /// </summary>
    private const string Wrapper =
        "out=$1; shift\n" +
        "if [ -n \"${BASH_VERSION:-}\" ]; then i=3; while [ \"$i\" -lt 256 ]; do eval \"exec $i>&-\"; i=$((i+1)); done; fi\n" +
        "exec \"$@\" >\"$out\" 2>&1 </dev/null\n";

    private const int SigTerm = 15;
    private const int SigKill = 9;

    private static readonly TimeSpan MainThreadWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BridgeWait = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan JoinWatch = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan QuitGrace = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(800);
    private const long FocusWatchMs = 15000;

    // What the copy logs when a join attempt fails, and the lines that say why
    // (ENetClient, JoinFlow and NJoinFriendScreen of v0.107.1, as in scripts/coop-bot.sh).
    private static readonly Regex JoinFailed = new(
        "Connection timed out!|Timed out waiting for handshake ack!|Received non-success code during handshake|" +
        "Received initial join message with failure|Received connection failed exception while joining game|" +
        "Received unexpected exception .* while joining game");

    private static readonly Regex JoinWhy = new(
        JoinFailed + "|Version mismatch\\. Host|Mod mismatch\\. Host|Mods that host has|Mods that we have|ModelDb hash mismatch");

    private static readonly Regex NoSteamArg = new("--force-steam[= ][Oo][Ff][Ff]");
    private static readonly Regex HostArg = new("--fastmp[= ](host|load)");
    private static readonly Regex ClientArg = new("--clientId[= ]([0-9]+)");

    /// <summary>The name a character answers to in the chat, by Character.Id.Entry, as Identity and the relay have it.</summary>
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IRONCLAD"] = "Ironclad",
        ["SILENT"] = "Silent",
        ["DEFECT"] = "Defect",
        ["REGENT"] = "Regent",
        ["NECROBINDER"] = "Necrobinder",
    };

    private static readonly (int Port, ulong NetId, string Character)[] NoCopies = [];

    private static readonly object Gate = new();
    private static readonly List<Copy> Copies = [];
    private static readonly SemaphoreSlim Starting = new(1, 1);
    // Loopback only: a proxy from the environment must never see these calls.
    private static readonly Lazy<System.Net.Http.HttpClient> Http = new(() =>
        new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) });

    private static volatile (int Port, ulong NetId, string Character)[] _known = NoCopies;
    private static volatile InstanceRole? _role;
    private static volatile bool _isBotCopy;
    private static volatile bool _shuttingDown;
    private static volatile bool _quiet;
    private static int _started;

    // The frame tick.
    private static int _hooked;
    private static ulong _lastFrame = ulong.MaxValue;
    private static bool _hostBroken;
    private static int _exitHooked;
    private static int _quitPatched;
    private static int _envLogged;

    // Taking the keyboard back after a start (main thread).
    private static long _focusUntil;
    private static long _focusFrom;
    private static long _unfocusedSince;
    private static long _lastRetake;
    private static int _retakes;
    private static bool _hadFocus;

    // The bot side.
    private static int _quitting;
    private static PosixSignalRegistration? _sigTerm;
    private static System.Threading.Timer? _hostWatch;
    private static int _hostMissing;
    private static int _hostWatchFailed;
    private static int _parentAtStart;

    /// <summary>A copy this process started.</summary>
    private sealed class Copy
    {
        public required ulong ClientId { get; init; }
        public required int Port { get; init; }
        public required string Character { get; init; } // the name it goes by, "" when none was asked for or it is the Random pick
        public required IChildProcess Process { get; init; }
        public required string LogPath { get; init; }
        public required string ConsolePath { get; init; }
        public volatile bool BridgeUp;
        public volatile bool AskedToQuit;
        public bool Alive => !Process.Exited.IsCompleted;
        public string Who => Character.Length > 0 ? $"the {Character}'s copy" : $"the copy on port {Port}";
    }

    // ---------------------------------------------------------------------------------------
    // The seam
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Starts one more bot copy that joins this game's lobby, and comes back when its bridge
    /// answers: "Started a bot copy as the Silent on port 17773 (player 1000). ...". On every
    /// failure the line says what is in the way and what to do about it. Never throws. Returns
    /// at once from any thread, the main thread included: the work runs on the pool, and its
    /// reads of the game are queued for the next frame ticks.
    /// </summary>
    /// <param name="character">The character the bot should play, as the relay passes it
    /// ("Silent", "Random"; "the Silent" and "SILENT" work too), or null to leave the pick to the bot.</param>
    /// <param name="ct">Stops the wait. A copy that was already started keeps running.</param>
    public static Task<string> StartCopyAsync(string? character, CancellationToken ct) =>
        Task.Run(() => StartGuardedAsync(character, ct), CancellationToken.None);

    /// <summary>
    /// The bot copies this copy started that are still running: bridge port, NetId (the copy's
    /// client id) and the name its character goes by ("Silent"; "" when none was asked for).
    /// A snapshot that changes only when a copy starts or ends: reading it costs nothing and
    /// never waits. Always empty in a solo game and in a bot copy.
    /// </summary>
    public static IReadOnlyList<(int Port, ulong NetId, string Character)> Known => _known;

    /// <summary>
    /// Shows that player's line as a speech bubble on this screen. The drawing belongs to
    /// <see cref="RemoteBubbles"/>, which names a player of this session by itself. A copy this
    /// game started that is not in the lobby yet (or was dropped from the run) has no player
    /// here, so the name it was started under goes along: its line then shows as
    /// "Silent: ..." instead of not at all.
    /// </summary>
    public static void ShowLine(ulong netId, string text)
    {
        string? name = null;
        foreach (var copy in _known)
        {
            if (copy.NetId != netId || copy.Character.Length == 0) continue;
            name = copy.Character;
            break;
        }
        RemoteBubbles.Show(netId, text, name);
    }

    // ---------------------------------------------------------------------------------------
    // More for the wiring
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// True only in a copy whose role says bot and that runs without Steam. Everything bot-only
    /// (the window, the quit handling) is behind this, so a stray <c>--sts2bot</c> in the
    /// human's Steam launch options can never make his own game hide its window or quit by itself.
    /// </summary>
    public static bool IsBotCopy => _isBotCopy;

    /// <summary>
    /// Tells this class which copy it is in. Call once from the mod's initializer, on the main
    /// thread, with the role the mod detected. In the human's copy it only remembers the role;
    /// nothing runs until a copy is asked for. In a bot copy it sets the window up, takes over
    /// SIGTERM, starts the watch on the game that started it and hooks its own frame tick.
    /// Never throws: a failure is logged and leaves the copy as it is.
    /// </summary>
    public static void Start(InstanceRole role)
    {
        if (role is null || Interlocked.Exchange(ref _started, 1) != 0) return;
        try
        {
            _role = role;
            if (!role.IsBot) return;

            BotArgs.Load();
            // Steam is brought up (or skipped) before mods load, so this is settled by now. A
            // copy with Steam plays from the real profile under steam/<id>: never treat it as a bot here.
            if (SteamInitializer.Initialized)
            {
                Log($"coop: {InstanceRole.BotArg} is set but this copy runs with Steam, so its window and quitting are left alone. A bot copy is started with --force-steam=off.", error: true);
                return;
            }

            _isBotCopy = true;
            Log($"coop: this is a bot copy: player {BotArgs.NetId}, bridge port {role.Port}, character {BotArgs.Character ?? "(none asked)"}, " +
                $"started by pid {BotArgs.HostPid?.ToString() ?? "(not given)"}");
            if (BotArgs.ClientId is null)
                Log($"coop: no usable --{BotArgs.ClientIdFlag}: the join would use {BotArgs.DefaultNetId} while the profile folder is default/1. Start bot copies with --{BotArgs.ClientIdFlag}=<number>.", error: true);
            else if (BotArgs.ClientId == HostNetId)
                Log($"coop: --{BotArgs.ClientIdFlag}=1 is the host's own id; this copy would collide with the host in a run.", error: true);
        }
        catch (Exception ex)
        {
            Log($"coop: working out whether this is a bot copy failed{(_isBotCopy ? "" : "; it runs as an ordinary window")}: {ex}", error: true);
            if (!_isBotCopy) return;
        }

        // Each part on its own: a window that can't be put away must not cost the clean quit, nor the other way round.
        BotStep("the window", BotWindow.Start);
        BotStep("the frame tick", () =>
        {
            if (!HookTick())
                Log("coop: the frame tick could not be hooked, so this copy's window is not put away (its quit handling still works)", error: true);
        });
        BotStep("the quit handling", BotSetup);
    }

    private static void BotStep(string what, Action step)
    {
        try { step(); }
        catch (Exception ex)
        {
            Log($"coop: setting up {what} of this bot copy failed (the rest still runs): {ex}", error: true);
        }
    }

    /// <summary>
    /// Asks every copy this game started to leave the lobby or run and quit (SIGTERM, which a
    /// bot copy answers that way), and waits up to ten seconds for them. Only one that is still
    /// there after that is ended the hard way, which leaves a ghost player on the host. Comes
    /// back with one line for the chat. Never throws.
    /// </summary>
    public static Task<string> StopAllAsync(CancellationToken ct) =>
        Task.Run(async () =>
        {
            try
            {
                Copy[] copies;
                lock (Gate) copies = Copies.Where(c => c.Alive).ToArray();
                if (copies.Length == 0) return "No bot copies of this game are running.";
                var lines = await Task.WhenAll(copies.Select(c => StopOneAsync(c, QuitGrace, ct))).ConfigureAwait(false);
                return string.Join(" ", lines.Select(Sentence));
            }
            catch (OperationCanceledException) { return "Stopped waiting for the copies to quit."; }
            catch (Exception ex)
            {
                Log($"coop: stopping the copies failed: {ex}", error: true);
                return $"Couldn't stop the copies: {ex.Message}";
            }
        }, CancellationToken.None);

    /// <summary><see cref="StopAllAsync"/> without waiting for the result. Returns at once.</summary>
    public static void StopAll() => _ = StopAllAsync(CancellationToken.None);

    /// <summary>
    /// In a bot copy: leave the lobby or run the way the game's own menus do (so the host
    /// drops this player instead of keeping a ghost), then quit the game. For SIGTERM, the
    /// host watch and anything else that wants a bot copy gone. Any thread; only the first
    /// call counts. Does nothing in the human's game.
    /// </summary>
    public static void RequestQuit(string why)
    {
        if (!IsBotCopy)
        {
            Log($"coop: ignored a request to quit ({why}): this is not a bot copy");
            return;
        }
        if (Interlocked.Exchange(ref _quitting, 1) != 0) return;
        Log($"coop: quitting this copy: {why}");
        _ = Task.Run(QuitAsync);
    }

    /// <summary>Where a copy stands, read on the main thread by <see cref="Look"/>.</summary>
    internal enum Place
    {
        /// <summary>The game is starting up, or between scenes.</summary>
        None,

        /// <summary>The main menu or one of its screens, with no lobby on top.</summary>
        Menu,

        /// <summary>Character select (standard, custom or daily) with a lobby.</summary>
        Lobby,

        /// <summary>The lobby of a saved co-op run being continued.</summary>
        LoadedLobby,

        /// <summary>A run, from its set-up at character select on.</summary>
        Run,
    }

    /// <summary>One look at the session. <see cref="Net"/> is the lobby's or the run's net service; null on the menu.</summary>
    internal sealed record Sight(Place Place, INetGameService? Net, StartRunLobby? Lobby, LoadRunLobby? Loaded, NSubmenu? Screen)
    {
        /// <summary>Host or client of a session with other players in it (or room for them).</summary>
        public bool Multiplayer => Net?.Type is NetGameType.Host or NetGameType.Client;

        /// <summary>A session over ENet on this Mac, which is the only kind a copy without Steam can join.</summary>
        public bool Local => Multiplayer && Net!.Platform == PlatformType.None;
    }

    /// <summary>
    /// Reads where this copy stands. Main thread only. The same reads work on the host and on a
    /// client; only <see cref="INetGameService.Type"/> differs. A lobby screen counts only on
    /// top of the main menu's submenu stack: the screens exist, hidden, long before they are used.
    /// </summary>
    internal static Sight Look()
    {
        var rm = RunManager.Instance;
        // NetService is null before the first run and stale after one: only read it while a run is in progress.
        if (rm is not null && rm.IsInProgress)
            return new Sight(Place.Run, rm.NetService, null, null, null);

        var game = NGame.Instance;
        if (game is null || !GodotObject.IsInstanceValid(game)) return new Sight(Place.None, null, null, null, null);
        var menu = game.MainMenu;
        if (menu is null) return new Sight(Place.None, null, null, null, null);

        var top = menu.SubmenuStack?.Peek();
        switch (top)
        {
            case NCharacterSelectScreen { Lobby: { } lobby }:
                return new Sight(Place.Lobby, lobby.NetService, lobby, null, top);
            case MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen { Lobby: { } lobby }:
                return new Sight(Place.Lobby, lobby.NetService, lobby, null, top);
            case MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen daily
                when Nodes.TryGetPrivate(daily, "_lobby") is StartRunLobby lobby:
                return new Sight(Place.Lobby, lobby.NetService, lobby, null, top);
            // The loaded-run screen keeps its lobby in a private field with no accessor.
            case NMultiplayerLoadGameScreen load when Nodes.TryGetPrivate(load, "_runLobby") is LoadRunLobby loaded:
                return new Sight(Place.LoadedLobby, loaded.NetService, null, loaded, top);
            default:
                return new Sight(Place.Menu, null, null, null, top);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Starting a copy
    // ---------------------------------------------------------------------------------------

    private static async Task<string> StartGuardedAsync(string? character, CancellationToken ct)
    {
        try
        {
            if (_role?.IsBot == true) return "This is a bot's copy of the game. Ask for bot copies in your own game.";
            await Starting.WaitAsync(ct).ConfigureAwait(false); // one start at a time: two at once would pick the same id and port
            try { return await StartAsync(character, ct).ConfigureAwait(false); }
            finally { Starting.Release(); }
        }
        catch (OperationCanceledException)
        {
            return "Stopped before a bot copy was started.";
        }
        catch (TimeoutException)
        {
            return "Can't start a bot copy: the game is busy and did not answer. Ask again in a moment.";
        }
        catch (Exception ex)
        {
            Log($"coop: starting a copy failed: {ex}", error: true);
            return $"Couldn't start a bot copy: {ex.Message} The game's log has the details.";
        }
    }

    /// <summary>What the host's game says about itself, read in one go on the main thread.</summary>
    private sealed record HostFacts(
        string? Version, SemanticVersion? SemVer, string ExePath, string UserDataDir, bool CanHostLocally,
        Place Place, NetGameType? NetType, bool Local, bool Connected, ulong LocalNetId,
        IReadOnlyList<ulong> PlayerIds, int MaxPlayers,
        IReadOnlyList<(ulong Id, string Entry)> Saved, IReadOnlyList<ulong> SavedConnected,
        IReadOnlyList<(string Entry, string Name, string Title)> Characters, string RandomEntry,
        IReadOnlyList<ModFact> Mods, string OwnModId, BotProfile.HostSettings Settings, bool Focused);

    /// <summary>A mod as one copy sees it. State is "loaded", "off" or "failed" once judged.</summary>
    private sealed class ModFact
    {
        public required string Id { get; init; }
        public string? Version { get; init; }
        public bool Gameplay { get; init; } = true;
        public bool Local { get; init; }
        public string Folder { get; init; } = "";
        public bool HasDll { get; init; }
        public string? MinGame { get; init; }
        public IReadOnlyList<(string Id, string? MinVersion)> Needs { get; init; } = [];
        public string? State { get; set; }
        public string Why { get; set; } = "";
        public bool Net { get; set; }

        /// <summary>The name the join check compares.</summary>
        public string Key => $"{Id}-{Version}";
        public string Label => $"{Id} {Version ?? "(no version)"}";
        public bool Matters => State == "loaded" && (Gameplay || Net);
    }

    private static async Task<string> StartAsync(string? asked, CancellationToken ct)
    {
        Log($"coop: asked for a bot copy{(string.IsNullOrWhiteSpace(asked) ? "" : $" ({asked.Trim()})")}");
        var facts = await OnMain(ReadHost, ct).ConfigureAwait(false);

        // 1. The game version the flags and the profile fields were checked against.
        var expected = System.Environment.GetEnvironmentVariable("SPIREMIND_COOP_GAME_VERSION") is { Length: > 0 } other ? other : CheckedGameVersion;
        if (facts.Version != expected)
            return Refuse($"this game is {facts.Version ?? "an unknown version"}, and a copy's launch flags, profile and join rules were only checked against {CheckedGameVersion}. " +
                          $"Check them again, then start the game with SPIREMIND_COOP_GAME_VERSION={facts.Version ?? "<version>"}.");

        // 2. A lobby a copy without Steam can join: this game hosting over ENet on this Mac, or able to.
        var (lobbyProblem, hosting) = LobbyCheck(facts);
        if (lobbyProblem is not null) return Refuse(lobbyProblem);

        // 3. Which character, which id and which port.
        var running = await ScanAsync(ct).ConfigureAwait(false);
        var (slot, slotProblem) = await PickSlotAsync(facts, asked, running, ct).ConfigureAwait(false);
        if (slot is null) return Refuse(slotProblem ?? "no free place for a copy.");
        var (clientId, port, entry, name) = slot.Value;

        // 4. The copy's profile as it is or as it will be made, and with it the mods the copy would load.
        var plan = BotProfile.Look(facts.UserDataDir, clientId, facts.Settings, facts.OwnModId);
        foreach (var note in plan.Notes) Log($"coop: profile: {note}");
        if (plan.Problem is not null) return Refuse(plan.Problem);
        if (ModsProblem(facts, plan) is { } modsProblem) return Refuse(modsProblem);

        // 5. Make what the profile lacks. Nothing that exists is replaced.
        if (BotProfile.Write(plan, facts.Settings, facts.UserDataDir) is { } writeProblem) return Refuse(writeProblem);

        // 6. The session may have changed while the files were looked at.
        ct.ThrowIfCancellationRequested();
        var again = await OnMain(ReadHost, ct).ConfigureAwait(false);
        (lobbyProblem, hosting) = LobbyCheck(again);
        if (lobbyProblem is not null) return Refuse(lobbyProblem);
        if (again.PlayerIds.Contains(clientId) || (again.Place == Place.LoadedLobby && again.SavedConnected.Contains(clientId)))
            return Refuse($"player {clientId} has joined your lobby in the meantime. Ask again.");

        // 7. Start it and wait for its bridge. Only now does this game get its exit hooks and the
        // frame tick for the focus watch: a request that was refused leaves the game as it was.
        await OnMain(HookHost, ct).ConfigureAwait(false);
        var copy = Launch(again, clientId, port, entry, name);
        _ = ModEntry.MainThread.Run(() => ArmFocusWatch(again.Focused));
        return await AwaitBridgeAsync(copy, hosting, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads everything a start needs from the running game. Main thread only. On the host
    /// these are all reads of its own state; on a client the same calls would work but
    /// <see cref="LobbyCheck"/> refuses there, since nobody can join a client.
    /// </summary>
    private static HostFacts ReadHost()
    {
        var sight = Look();
        var net = sight.Net;
        var ids = new List<ulong>();
        var saved = new List<(ulong, string)>();
        var savedConnected = new List<ulong>();
        var maxPlayers = 0;
        if (sight.Lobby is { } lobby)
        {
            // Players is a list of structs that the lobby changes in place: copy what is needed now.
            foreach (var player in lobby.Players.ToArray()) ids.Add(player.id);
            maxPlayers = lobby.MaxPlayers;
        }
        else if (sight.Loaded is { } loaded)
        {
            foreach (var player in loaded.Run.Players) saved.Add((player.NetId, player.CharacterId?.Entry ?? ""));
            savedConnected.AddRange(loaded.ConnectedPlayerIds);
        }

        var characters = new List<(string, string, string)>();
        foreach (var character in ModelDb.AllCharacters)
            if (character.Id.Entry is { Length: > 0 } id)
                characters.Add((id, NameOf(character), Describe.SafeText(() => character.Title)));

        var mods = new List<ModFact>();
        string? own = null;
        foreach (var mod in ModManager.Mods)
        {
            if (mod.manifest?.id is not { } id) continue;
            if (ReferenceEquals(mod.assembly, typeof(ModEntry).Assembly)) own = id;
            mods.Add(new ModFact
            {
                Id = id,
                Version = mod.manifest.version,
                Gameplay = mod.manifest.affectsGameplay,
                Local = mod.modSource == ModSource.ModsDirectory,
                Folder = mod.path ?? "",
                HasDll = mod.manifest.hasDll,
                Needs = mod.manifest.dependencies?.Select(d => (d.id, d.minVersion)).ToList() ?? [],
                State = mod.state == ModLoadState.Loaded ? "loaded" : "off",
            });
        }

        return new HostFacts(
            Version: ReleaseInfoManager.Instance.ReleaseInfo?.Version,
            SemVer: ReleaseInfoManager.Instance.SemVer,
            ExePath: OS.GetExecutablePath(),
            UserDataDir: OS.GetUserDataDir(),
            // The game hosts over ENet exactly when Steam is off or any --fastmp flag is present (NMultiplayerHostSubmenu.StartHostAsync).
            CanHostLocally: !SteamInitializer.Initialized || CommandLineHelper.HasArg("fastmp"),
            Place: sight.Place,
            NetType: net?.Type,
            Local: sight.Local,
            Connected: net?.IsConnected == true,
            LocalNetId: sight.Multiplayer && net!.IsConnected ? net.NetId : HostNetId,
            PlayerIds: ids,
            MaxPlayers: maxPlayers,
            Saved: saved,
            SavedConnected: savedConnected,
            Characters: characters,
            RandomEntry: ModelDb.Character<RandomCharacter>().Id.Entry ?? "RANDOM_CHARACTER",
            Mods: mods,
            OwnModId: own ?? "SpireMind",
            Settings: BotProfile.Capture(),
            Focused: DisplayServer.WindowIsFocused());
    }

    /// <summary>
    /// Whether a copy without Steam could join this game. Problem is what the human must do
    /// first, or null when a start makes sense: either this game is hosting a local (ENet)
    /// lobby right now (Hosting true), or it would host one as soon as he opens it (Hosting
    /// false; the copy keeps looking for the lobby).
    /// </summary>
    private static (string? Problem, bool Hosting) LobbyCheck(HostFacts facts)
    {
        const string viaSteam = "Quit the game, add --fastmp host_standard to its launch options in Steam (Properties, General, Launch Options), start it again and ask from the co-op character select.";
        const string notYours = "you joined someone else's game, and a bot copy can only join a game you host on this Mac.";
        var steamOnly = $"this game would host its lobby over Steam, which a copy without Steam can't join. {viaSteam}";

        switch (facts.Place)
        {
            case Place.None:
                return ("the game is still starting up or between screens. Ask again in a moment.", false);
            case Place.Run:
                return (facts.NetType switch
                {
                    NetGameType.Host => "your co-op run has already started, and nobody can join a run in progress. Start bot copies while you wait in character select.",
                    NetGameType.Client => notYours,
                    _ => "you are in a solo run, and a bot copy can only join a co-op lobby. Finish or leave the run, host a co-op game (Multiplayer, then Host) and ask again from character select.",
                }, false);
            case Place.Menu:
                return facts.CanHostLocally ? (null, false) : (steamOnly, false);
        }

        // A lobby screen is up.
        if (facts.NetType == NetGameType.Client) return (notYours, false);
        if (facts.NetType != NetGameType.Host)
            return facts.CanHostLocally ? (null, false) : (steamOnly, false); // the solo character select
        if (!facts.Local) return ($"your lobby is a Steam lobby, which a copy without Steam can't join. {viaSteam}", false);
        if (!facts.Connected)
            return ("your lobby is not listening (its network port could not be opened; the game's log says \"Failed to create host!\"). Go back and host again.", false);
        if (facts.Place == Place.Lobby && facts.MaxPlayers > 0 && facts.PlayerIds.Count >= facts.MaxPlayers)
            return ($"your lobby is full ({facts.PlayerIds.Count} players).", false);
        return (null, true);
    }

    /// <summary>
    /// Picks the client id, the port and the character for the next copy: 1000 on 17773, then
    /// 1001 on 17774, then 1002 on 17775. A place is taken when a copy of ours holds it, a game
    /// process runs with that id, a bridge answers on the port, or something else listens
    /// there. In the lobby of a saved run the ids and characters are the save's.
    /// </summary>
    private static async Task<((ulong ClientId, int Port, string? Entry, string Name)? Slot, string? Problem)> PickSlotAsync(
        HostFacts facts, string? asked, (bool Ok, List<Running> Copies) running, CancellationToken ct)
    {
        var (entry, name, characterProblem) = ResolveCharacter(facts, asked);
        if (characterProblem is not null) return (null, characterProblem);

        async Task<string?> TakenAsync(ulong id, int port)
        {
            lock (Gate)
                if (Copies.FirstOrDefault(c => c.Alive && (c.ClientId == id || c.Port == port)) is { } ours)
                    return $"{ours.Who} is running (player {ours.ClientId}, port {ours.Port})";
            if (running.Copies.FirstOrDefault(r => r.ClientId == id) is { } process)
                return $"a copy with client id {id} is already running (pid {process.Pid})";
            if (await ProbeAsync(port, ProbeTimeout, ct).ConfigureAwait(false))
                return $"a SpireMind bridge already answers on port {port}";
            return PortFree(port) ? null : $"port {port} is in use by another program";
        }

        if (facts.Place == Place.LoadedLobby)
        {
            // A saved run: the host refuses any id the save doesn't know, and the save fixes the characters.
            var open = facts.Saved.Where(p => p.Id != facts.LocalNetId && !facts.SavedConnected.Contains(p.Id)).ToList();
            var listed = string.Join(", ", facts.Saved.Where(p => p.Id != facts.LocalNetId).Select(p => $"{NameOf(p.Entry)} (player {p.Id})"));
            if (open.Count == 0)
                return (null, listed.Length == 0 ? "this saved run has no other players." : $"everyone in this saved run is already connected ({listed}).");
            if (entry is not null)
            {
                open = open.Where(p => string.Equals(p.Entry, entry, StringComparison.OrdinalIgnoreCase)).ToList();
                if (open.Count == 0) return (null, $"this saved run has no free {name}. Its other players are: {listed}.");
            }
            foreach (var player in open)
            {
                if (player.Id == HostNetId || PortFor(player.Id) is not { } port)
                {
                    Log($"coop: player {player.Id} of the saved run is not one of the bot ids {FirstClientId} to {FirstClientId + MaxCopies - 1}; skipped");
                    continue;
                }
                if (await TakenAsync(player.Id, port).ConfigureAwait(false) is { } taken)
                {
                    Log($"coop: player {player.Id} of the saved run: {taken}");
                    continue;
                }
                return ((player.Id, port, IsRealCharacter(facts, player.Entry) ? player.Entry : null, NameOf(player.Entry)), null);
            }
            return (null, $"the saved run's free players can't be started as bot copies (other ids, a copy already running, or a port taken): {listed}. The log says which.");
        }

        var reasons = new List<string>();
        for (var k = 0; k < MaxCopies; k++)
        {
            var id = FirstClientId + (ulong)k;
            var port = FirstPort + k;
            if (await TakenAsync(id, port).ConfigureAwait(false) is { } taken)
            {
                reasons.Add(taken);
                continue;
            }
            if (facts.PlayerIds.Contains(id))
            {
                // In the lobby, but nothing of it runs any more: the host never drops a player that vanished.
                if (running.Ok)
                    return (null, $"your lobby still lists player {id} from a copy that is no longer running (a ghost), and the run can't start while it is there. Go back out of the lobby, host again and ask again.");
                reasons.Add($"player {id} is in your lobby");
                continue;
            }
            return ((id, port, entry, name), null);
        }
        return (null, $"all {MaxCopies} places for bot copies are taken: {string.Join("; ", reasons)}.");
    }

    /// <summary>Turns what was asked for into a Character.Id.Entry and the name it goes by. No character asked for is fine.</summary>
    private static (string? Entry, string Name, string? Problem) ResolveCharacter(HostFacts facts, string? asked)
    {
        var text = asked?.Trim() ?? "";
        if (text.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) text = text[4..].Trim();
        if (text.Length == 0) return (null, "", null);
        if (text.Equals("random", StringComparison.OrdinalIgnoreCase) || text.Equals(facts.RandomEntry, StringComparison.OrdinalIgnoreCase))
            return (facts.RandomEntry, "", null);

        foreach (var (entry, name, title) in facts.Characters)
        {
            var bare = title.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? title[4..].Trim() : title;
            if (text.Equals(entry, StringComparison.OrdinalIgnoreCase) || text.Equals(name, StringComparison.OrdinalIgnoreCase)
                || text.Equals(bare, StringComparison.OrdinalIgnoreCase) || text.Equals(entry.Replace('_', ' '), StringComparison.OrdinalIgnoreCase))
                return (entry, name, null);
        }
        // Never echo more than a word of what was typed back into the chat.
        var shown = text.Length > 24 ? text[..24] + "..." : text;
        return (null, "", $"there is no character called \"{shown}\". The choices are {string.Join(", ", facts.Characters.Select(c => c.Name))}, or Random.");
    }

    private static bool IsRealCharacter(HostFacts facts, string entry) =>
        facts.Characters.Any(c => string.Equals(c.Entry, entry, StringComparison.OrdinalIgnoreCase));

    /// <summary>The port of the bot with this client id; null for any other id.</summary>
    private static int? PortFor(ulong clientId) =>
        clientId is >= FirstClientId and < FirstClientId + MaxCopies ? FirstPort + (int)(clientId - FirstClientId) : null;

    /// <summary>
    /// The name a character goes by in the chat: "Silent" for SILENT, as <see cref="Identity"/>
    /// names the local bot, so the human's copy and the bot's copy agree. Other characters use
    /// their title without "The". Main thread only when a title has to be read.
    /// </summary>
    private static string NameOf(CharacterModel character)
    {
        var id = character.Id.Entry ?? "";
        if (Names.TryGetValue(id, out var name)) return name;
        var title = Describe.SafeText(() => character.Title);
        if (title.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) title = title[4..];
        title = title.Trim();
        return title.Length > 0 ? title : NameOf(id);
    }

    /// <summary>The same from the id alone, for when there is no model at hand: "SILENT" becomes "Silent". Any thread.</summary>
    private static string NameOf(string entry)
    {
        if (Names.TryGetValue(entry, out var name)) return name;
        if (entry.Length == 0) return "";
        var words = entry.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(' ', words);
    }

    private static string Refuse(string why)
    {
        Log($"coop: not starting a copy: {why}");
        return "Can't start a bot copy: " + why;
    }

    // ---------------------------------------------------------------------------------------
    // Mods: both copies must load the same gameplay mods and the same network message types
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Why the copy could not join because of mods, or null. The host's side is what its
    /// ModManager really loaded. The copy's side is predicted the way ModManager of v0.107.1
    /// decides (as scripts/coop-bot.sh does): it only sees the local mods folder, which is the
    /// source of truth here; a mod is off when its settings say so; a mod needs its minimum
    /// game version and its dependencies. The join compares "id-version" of every loaded mod
    /// that affects gameplay; a mod that adds network message types must match too, or the two
    /// copies number their messages differently.
    /// </summary>
    private static string? ModsProblem(HostFacts facts, BotProfile.Plan plan)
    {
        var exeDir = Path.GetDirectoryName(facts.ExePath) ?? "";
        var modsDir = Path.Combine(exeDir, "mods");

        var host = facts.Mods;
        var bot = ReadManifests(modsDir, local: true);
        bot.AddRange(ReadManifests(Path.Combine(exeDir, "mods_STEAMTEST"), local: false));
        Judge(bot, plan, facts.SemVer);
        foreach (var mod in host.Concat(bot))
            if (mod.State == "loaded" && mod.HasDll)
                mod.Net = NamesNetTypes(Path.Combine(mod.Folder, mod.Id + ".dll"));

        Log("coop: mods: this game loaded: " + Listing(host));
        Log("coop: mods: a copy would load: " + Listing(bot));

        var mine = bot.FirstOrDefault(m => m.Id == facts.OwnModId && m.State == "loaded") ?? bot.FirstOrDefault(m => m.Id == facts.OwnModId);
        if (mine is null)
            return $"{facts.OwnModId} is not in the game's own mods folder ({modsDir}), and a copy without Steam only loads mods from there. Put it there (scripts/install.sh with the game closed).";
        if (mine.State != "loaded")
            return mine.State == "off"
                ? $"the copy's profile has {facts.OwnModId} off ({mine.Why}). Delete {plan.SettingsPath} and ask again; it is made afresh from your settings."
                : $"a copy would not load {facts.OwnModId} ({mine.Why}).";

        var hostSet = new Dictionary<string, ModFact>();
        foreach (var mod in host.Where(m => m.Matters)) hostSet[mod.Key] = mod;
        var botSet = new Dictionary<string, ModFact>();
        foreach (var mod in bot.Where(m => m.Matters)) botSet[mod.Key] = mod;
        var onlyHost = hostSet.Values.Where(m => !botSet.ContainsKey(m.Key)).ToList();
        var onlyBot = botSet.Values.Where(m => !hostSet.ContainsKey(m.Key)).ToList();
        if (onlyHost.Count == 0 && onlyBot.Count == 0)
        {
            Log("coop: mods: both copies would load the same gameplay mods and network message types");
            return null;
        }

        var parts = new List<string>();
        var workshop = onlyHost.Where(m => !m.Local).ToList();
        if (workshop.Count > 0)
        {
            parts.Add($"your game loads {string.Join(" and ", workshop.Select(m => m.Label))} from the Steam Workshop, which a copy without Steam never sees, so its join would be refused. " +
                      $"For each, either copy its folder into {modsDir} (a local copy wins in your game too) or switch it off in Settings, then restart your game. The log has the folders.");
            foreach (var mod in workshop)
            {
                var users = host.Where(m => m.State == "loaded" && m.Needs.Any(n => n.Id == mod.Id)).Select(m => m.Label).ToList();
                Log($"coop: mods: {mod.Label} (Workshop{(mod.Gameplay ? ", changes gameplay: the join is refused with ModMismatch" : "")}" +
                    $"{(mod.Net ? ", adds network message types: the join breaks without a clear error" : "")}) is in {mod.Folder}; " +
                    $"a local copy would go to {Path.Combine(modsDir, mod.Id)}; {(users.Count > 0 ? "needed by " + string.Join(", ", users) : "no other loaded mod needs it")}");
            }
        }
        var local = onlyHost.Where(m => m.Local).ToList();
        if (local.Count > 0 || onlyBot.Count > 0)
        {
            var differences = local.Select(m => $"{m.Label} is loaded by your game but not by a copy{Reason(bot, m)}")
                .Concat(onlyBot.Select(m => $"{m.Label} would be loaded by a copy but not by your game"));
            parts.Add($"the two copies disagree about local mods: {string.Join("; ", differences)}. " +
                      (plan.SettingsExist
                          ? $"If the copy's profile is out of date, delete {plan.SettingsPath} and ask again; it is made afresh from your settings. Mods changed on disk need a restart of your game."
                          : "Mods load only when a game starts, so restart your game and ask again."));
        }
        return string.Join(" Also, ", parts);
    }

    private static string Reason(List<ModFact> bot, ModFact hostMod)
    {
        var there = bot.FirstOrDefault(m => m.Id == hostMod.Id);
        return there is null ? " (not in the mods folder now)"
            : there.State != "loaded" ? $" ({there.Why})"
            : $" (a copy would load {there.Label})";
    }

    private static string Listing(IEnumerable<ModFact> mods)
    {
        var lines = mods.Select(m => m.State == "loaded"
            ? $"{m.Label} [{(m.Local ? "local" : "Workshop")}{(m.Gameplay ? ", changes gameplay" : "")}{(m.Net ? ", adds network message types" : "")}]"
            : $"{m.Label} [not loaded{(m.Why.Length > 0 ? ": " + m.Why : "")}]").ToList();
        return lines.Count == 0 ? "nothing" : string.Join("; ", lines);
    }

    /// <summary>Every *.json under a mods folder that has an id is a manifest, as the game reads them.</summary>
    private static List<ModFact> ReadManifests(string root, bool local)
    {
        var mods = new List<ModFact>();
        if (!Directory.Exists(root)) return mods;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return mods; }

        foreach (var file in files)
        {
            JsonObject? json;
            try { json = JsonNode.Parse(File.ReadAllText(file)) as JsonObject; }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { continue; }
            if (json is null || Str(json["id"]) is not { } id) continue;

            var needs = new List<(string, string?)>();
            if (json["dependencies"] is JsonArray dependencies)
                foreach (var dependency in dependencies)
                {
                    if (Str(dependency) is { } bare) needs.Add((bare, null));
                    else if (dependency is JsonObject o && Str(o["id"]) is { } depId) needs.Add((depId, Str(o["min_version"])));
                }
            mods.Add(new ModFact
            {
                Id = id,
                Version = Str(json["version"]),
                Gameplay = !(json["affects_gameplay"] is JsonValue g && g.TryGetValue<bool>(out var affects) && !affects),
                Local = local,
                Folder = Path.GetDirectoryName(file) ?? root,
                HasDll = json["has_dll"] is JsonValue d && d.TryGetValue<bool>(out var hasDll) && hasDll,
                MinGame = Str(json["min_game_version"]),
                Needs = needs,
            });
        }
        return mods;
    }

    /// <summary>Decides which of the copy's mods load, in ModManager's order of checks.</summary>
    private static void Judge(List<ModFact> mods, BotProfile.Plan plan, SemanticVersion? game)
    {
        var localOn = new HashSet<string>();
        foreach (var mod in mods)
        {
            if (plan.Mods.Any(c => c.Id == mod.Id && c.Local == mod.Local && !c.Enabled))
                (mod.State, mod.Why) = ("off", "switched off in its settings");
            else if (mod.Local)
                localOn.Add(mod.Id);
        }
        foreach (var mod in mods)
            if (mod.State is null && !mod.Local && localOn.Contains(mod.Id))
                (mod.State, mod.Why) = ("off", "the local copy is used instead");

        var active = mods.Where(m => m.State is null).ToList();
        if (!plan.ModsEnabled)
        {
            foreach (var mod in active) (mod.State, mod.Why) = ("off", "its settings have not agreed to load mods");
            return;
        }

        var loaded = new HashSet<string>();
        bool Fail(ModFact mod, string why)
        {
            (mod.State, mod.Why) = ("failed", why);
            return false;
        }
        bool Resolve(ModFact mod, HashSet<string> chain)
        {
            if (mod.State is not null) return mod.State == "loaded";
            if (chain.Contains(mod.Id)) return Fail(mod, "circular dependency");
            if (loaded.Contains(mod.Id)) return Fail(mod, "another copy with this id is loaded");
            if (mod.MinGame is not null && game is not null)
            {
                if (!SemanticVersion.TryFromString(mod.MinGame, out var need) || need is null) return Fail(mod, $"its minimum game version {mod.MinGame} can't be read");
                if (game.CompareTo(need) < 0) return Fail(mod, $"needs game version {mod.MinGame}");
            }
            foreach (var (depId, minVersion) in mod.Needs)
            {
                var dep = mods.FirstOrDefault(m => m.Id == depId);
                if (dep is null || !Resolve(dep, [.. chain, mod.Id])) return Fail(mod, $"needs {depId}, which is not loaded");
                if (minVersion is null) continue;
                if (!SemanticVersion.TryFromString(minVersion, out var need) || need is null
                    || dep.Version is null || !SemanticVersion.TryFromString(dep.Version, out var have) || have is null
                    || have.CompareTo(need) < 0)
                    return Fail(mod, $"needs {depId} {minVersion} or newer");
            }
            mod.State = "loaded";
            loaded.Add(mod.Id);
            return true;
        }
        foreach (var mod in active) Resolve(mod, []);
    }

    /// <summary>A mod that defines network message or action types names these interfaces in its DLL.</summary>
    private static bool NamesNetTypes(string dll)
    {
        try
        {
            var info = new FileInfo(dll);
            if (!info.Exists || info.Length > 64L << 20) return false;
            ReadOnlySpan<byte> data = File.ReadAllBytes(dll);
            // Split, so this mod's own DLL doesn't hold the two names it looks for.
            return data.IndexOf(Encoding.ASCII.GetBytes("INet" + "Message")) >= 0 || data.IndexOf(Encoding.ASCII.GetBytes("INet" + "Action")) >= 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------
    // The process
    // ---------------------------------------------------------------------------------------

    private static Copy Launch(HostFacts facts, ulong clientId, int port, string? entry, string name)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        // Out of the game's data folder: logs/godot.log there is this game's own live log.
        var logDir = Path.Combine(home, "sts2bot");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, $"godot-{clientId}.log");
        var consolePath = logPath + ".out";
        // The game starts its log file afresh. Keep the last one, so a failed try can still be read.
        try { if (new FileInfo(logPath) is { Exists: true, Length: > 0 }) File.Move(logPath, logPath + ".prev", overwrite: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* then it is overwritten */ }

        var (size, position) = BotWindow.TileFor(clientId);
        var number = System.Globalization.CultureInfo.InvariantCulture;
        // "=" for every flag the game or the mod reads, so no value can be taken for another flag;
        // two tokens for the engine's own flags, which it consumes before the game sees the list.
        var args = new List<string>
        {
            "-c", Wrapper, "sh", consolePath, facts.ExePath,
            "--force-steam=off",                                           // the only thing that keeps a copy from starting Steam
            BotArgs.Token(BotArgs.ClientIdFlag, clientId.ToString(number)), // its NetId in the lobby and its profile folder default/<id>
            "--fastmp=join",                                               // skips the logo; every main menu of the copy makes one join attempt
            $"{InstanceRole.BotArg}=1",                                    // the mod's role: a bot copy
            BotArgs.Token(BotArgs.HostPidFlag, System.Environment.ProcessId.ToString(number)),
        };
        if (entry is not null) args.Add(BotArgs.Token(BotArgs.CharacterFlag, entry));
        args.AddRange(["--log-file", logPath]);                             // else the copy rotates this game's live log
        args.AddRange(["--resolution", $"{size.X}x{size.Y}"]);              // the first window, before the copy's settings are applied
        args.AddRange(["--position", $"{position.X},{position.Y}"]);

        // The copy inherits this game's environment. Steam injects its overlay through
        // DYLD_INSERT_LIBRARIES (the game is signed to honour it), and the engine forces a new
        // window to the front unless __CFBundleIdentifier is the app's own id.
        var env = new Dictionary<string, string?>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var key in System.Environment.GetEnvironmentVariables().Keys)
        {
            if (key is not string variable) continue;
            names.Add(variable);
            if (variable == "DYLD_INSERT_LIBRARIES" || variable.StartsWith("Steam", StringComparison.Ordinal) || variable.StartsWith("STEAM", StringComparison.Ordinal))
                env[variable] = null;
        }
        names.Sort(StringComparer.Ordinal);
        // Names only, never values, and once a session: which variables Steam really sets here is one of the open questions.
        if (Interlocked.Exchange(ref _envLogged, 1) == 0)
            Log($"coop: this game's environment variables (names only): {string.Join(" ", names)}");
        Log($"coop: removed for the copy: {(env.Count == 0 ? "none" : string.Join(" ", env.Keys.OrderBy(k => k, StringComparer.Ordinal)))}");
        env["__CFBundleIdentifier"] = BundleId;
        env["SPIREMIND_PORT"] = port.ToString(number);
        // The copy keeps its own files in ~/.config/spiremind/bot-<port>, never this game's folder.
        env[InstanceRole.ConfigDirEnvVar] = null;

        // The copy's working folder: without Steam the game looks for mp_names.json there, nothing else.
        var process = ProcessLauncher.Instance.Start(new ChildProcessSpec("/bin/sh", args, logDir, env));
        var copy = new Copy
        {
            ClientId = clientId,
            Port = port,
            Character = name,
            Process = process,
            LogPath = logPath,
            ConsolePath = consolePath,
        };
        lock (Gate)
        {
            Copies.Add(copy);
            PublishKnown();
        }
        Log($"coop: started a copy (pid {process.Id}) as player {clientId} on port {port}{(entry is null ? "" : $" for {entry}")}: " +
            $"{facts.ExePath} {string.Join(' ', args.Skip(5))}; log {logPath}");
        _ = process.Exited.ContinueWith(done => OnExited(copy, done.Result), TaskScheduler.Default);
        if (_shuttingDown) AskToQuit(copy); // this game began to close while the copy was being started
        return copy;
    }

    /// <summary>Caller holds <see cref="Gate"/>. The snapshot <see cref="Known"/> hands out.</summary>
    private static void PublishKnown() =>
        _known = Copies.Count == 0 ? NoCopies : Copies.OrderBy(c => c.Port).Select(c => (c.Port, c.ClientId, c.Character)).ToArray();

    private static async Task<string> AwaitBridgeAsync(Copy copy, bool hosting, CancellationToken ct)
    {
        var who = copy.Character.Length > 0 ? $" as the {copy.Character}" : "";
        var started = System.Environment.TickCount64;
        try
        {
            while (true)
            {
                if (await ProbeAsync(copy.Port, ProbeTimeout, ct).ConfigureAwait(false)) break;
                if (!copy.Alive)
                {
                    foreach (var line in Tail(copy, 12)) Log($"coop: copy {copy.ClientId}: {line}");
                    return $"The bot copy{who} quit before its bridge answered. The end of its log is in this game's log; the whole of it is {LogOf(copy)}.";
                }
                if (System.Environment.TickCount64 - started > BridgeWait.TotalMilliseconds)
                    return await BridgeTimedOutAsync(copy, who, ct).ConfigureAwait(false);
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return $"A bot copy{who} was started on port {copy.Port} (player {copy.ClientId}), but the wait for its bridge was stopped.";
        }

        copy.BridgeUp = true;
        Log($"coop: copy {copy.ClientId}: its bridge answers on port {copy.Port} after {System.Environment.TickCount64 - started} ms");
        _ = Task.Run(() => WatchJoinAsync(copy));
        return $"Started a bot copy{who} on port {copy.Port} (player {copy.ClientId}). " +
               (hosting
                   ? "It joins your lobby by itself."
                   : "No co-op lobby is open yet: choose Multiplayer, then Host, and it joins by itself.");
    }

    /// <summary>
    /// No bridge after a minute: the mod did not load in the copy (its profile had not agreed
    /// to mods) or could not listen. A copy that is not in the lobby is closed again. One that
    /// did get in is left alone, because ending it would leave a ghost player.
    /// </summary>
    private static async Task<string> BridgeTimedOutAsync(Copy copy, string who, CancellationToken ct)
    {
        foreach (var line in Tail(copy, 12)) Log($"coop: copy {copy.ClientId}: {line}");
        var inLobby = false;
        try { inLobby = await OnMain(() => InSession(copy.ClientId), ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* unknown: treat as not in the lobby */ }

        if (inLobby)
            return $"The bot copy{who} is in your lobby as player {copy.ClientId}, but its bridge on port {copy.Port} never answered, so no bot can play in it. " +
                   $"Its window is in the Dock; its log is {LogOf(copy)}.";
        var ended = await StopOneAsync(copy, TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        return $"The bridge of the bot copy{who} on port {copy.Port} did not answer within {(int)BridgeWait.TotalSeconds} s, so it was closed again ({ended}). " +
               $"Most likely the mod did not load there; see {LogOf(copy)}.";
    }

    /// <summary>
    /// After the bridge is up the copy joins by itself. For the log of a first test: watches
    /// for the player turning up in this game's lobby, and copies the lines of the copy's log
    /// that say why a join attempt failed.
    /// </summary>
    private static async Task WatchJoinAsync(Copy copy)
    {
        try
        {
            var deadline = System.Environment.TickCount64 + (long)JoinWatch.TotalMilliseconds;
            long offset = 0;
            var carry = "";
            var reported = 0;
            while (System.Environment.TickCount64 < deadline && copy.Alive && !_shuttingDown)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                if (await OnMain(() => InSession(copy.ClientId), CancellationToken.None).ConfigureAwait(false))
                {
                    Log($"coop: copy {copy.ClientId} is in the lobby");
                    return;
                }

                var why = new List<string>();
                var failed = false;
                foreach (var line in NewLines(LogOf(copy), ref offset, ref carry))
                {
                    if (JoinWhy.IsMatch(line) && why.Count < 6) why.Add(line.Length > 240 ? line[..240] : line);
                    failed |= JoinFailed.IsMatch(line);
                }
                if (!failed || reported >= 3) continue;
                reported++;
                foreach (var line in why) Log($"coop: copy {copy.ClientId}: {line}");
                Log($"coop: copy {copy.ClientId}: a join attempt failed: {JoinAdvice(string.Join('\n', why), copy.ClientId)}", error: true);
            }
            if (copy.Alive && !_shuttingDown)
                Log($"coop: copy {copy.ClientId}: not in a lobby of this game after {(int)JoinWatch.TotalSeconds} s; its log is {LogOf(copy)}");
        }
        catch (Exception ex)
        {
            Log($"coop: watching copy {copy.ClientId} join failed: {ex.Message}", error: true);
        }
    }

    /// <summary>One sentence of advice for the failure the copy's log lines show.</summary>
    private static string JoinAdvice(string why, ulong clientId)
    {
        if (why.Contains("Connection timed out!", StringComparison.Ordinal))
            return "nobody answered on the lobby's port: this game was not in the co-op character select.";
        if (why.Contains("non-success code during handshake", StringComparison.Ordinal))
            return $"the lobby already holds a player {clientId}, the ghost of an earlier copy: go back out of the lobby and host again.";
        if (why.Contains("Mod mismatch", StringComparison.Ordinal) || why.Contains("Mods that host has", StringComparison.Ordinal))
            return "the two copies loaded different gameplay mods: close both and start again; the lines above list both sides.";
        if (why.Contains("ModelDb hash mismatch", StringComparison.Ordinal))
            return "a mod on one side adds cards, relics or the like: make that mod the same on both sides.";
        if (why.Contains("Version mismatch", StringComparison.Ordinal))
            return "the two copies are different game versions: quit both and start again.";
        if (why.Contains("RunInProgress", StringComparison.Ordinal))
            return "the run had already started: a copy can only join in character select.";
        if (why.Contains("NotInSaveGame", StringComparison.Ordinal))
            return $"the saved run that was loaded does not know player {clientId}.";
        return "the lines above say why.";
    }

    /// <summary>Whether this game's lobby or run holds that player right now. Main thread only; a read on the host's own state.</summary>
    private static bool InSession(ulong clientId)
    {
        var sight = Look();
        if (sight.Lobby is { } lobby) return lobby.Players.ToArray().Any(p => p.id == clientId);
        if (sight.Loaded is { } loaded) return loaded.ConnectedPlayerIds.Contains(clientId);
        return sight.Place == Place.Run && RunManager.Instance.DebugOnlyGetState()?.GetPlayer(clientId) is not null;
    }

    private static void OnExited(Copy copy, int code)
    {
        lock (Gate)
        {
            Copies.Remove(copy);
            PublishKnown();
        }
        var errors = new List<string>();
        while (errors.Count < 6 && copy.Process.StandardError.TryRead(out var line)) errors.Add(line);
        try { copy.Process.Dispose(); }
        catch (Exception) { /* already gone */ }
        if (_shuttingDown) return;

        Log($"coop: copy {copy.ClientId} (port {copy.Port}) exited with code {code}{(errors.Count > 0 ? ": " + string.Join(" | ", errors) : "")}");
        // A copy that had come up and was not asked to go: it crashed or someone closed its window.
        if (copy.BridgeUp && !copy.AskedToQuit)
            Log($"coop: {copy.Who} has quit by itself. If it was in the lobby or run it is still listed there as a ghost: " +
                $"go back out and host again before starting another. Its log is {LogOf(copy)}.", error: true);
    }

    // ---------------------------------------------------------------------------------------
    // Stopping
    // ---------------------------------------------------------------------------------------

    /// <summary>Asks one copy to quit and waits for it to be gone. Returns a clause without a capital or a full stop.</summary>
    private static async Task<string> StopOneAsync(Copy copy, TimeSpan grace, CancellationToken ct)
    {
        if (!copy.Alive) return $"{copy.Who} has quit";
        // A bot copy takes SIGTERM as "leave, then quit". A copy without the mod just ends.
        AskToQuit(copy);
        Log($"coop: sent SIGTERM to {copy.Who} (pid {copy.Process.Id})");

        var deadline = System.Environment.TickCount64 + (long)grace.TotalMilliseconds;
        while (System.Environment.TickCount64 < deadline)
        {
            if (!copy.Alive)
            {
                Log($"coop: {copy.Who} has quit");
                return $"{copy.Who} has quit";
            }
            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        // It did not go. Ending it leaves its player on the host as a ghost, but a copy that hangs is no better.
        copy.Process.Kill();
        Log($"coop: {copy.Who} did not quit within {(int)grace.TotalSeconds} s and was ended", error: true);
        return $"{copy.Who} did not quit when asked and was ended; if it was in your lobby or run it is still listed there, and only hosting again clears it";
    }

    private static void AskToQuit(Copy copy)
    {
        if (!copy.Alive || copy.AskedToQuit) return;
        copy.AskedToQuit = true;
        if (!Signal(copy.Process.Id, SigTerm)) copy.Process.Kill();
    }

    /// <summary>
    /// This game is closing: every copy it started goes too, so none is left behind without a
    /// host. Each gets SIGTERM at once, which a bot copy answers by leaving and quitting. With
    /// <paramref name="wait"/> (the process is exiting) stragglers get two seconds, then they are ended.
    /// </summary>
    private static void EndCopies(bool wait)
    {
        _shuttingDown = true;
        Copy[] copies;
        lock (Gate) copies = Copies.Where(c => c.Alive).ToArray();
        if (copies.Length == 0) return;
        Log($"coop: this game is closing; asking {copies.Length} bot copy(ies) to quit");
        foreach (var copy in copies) AskToQuit(copy);
        if (!wait) return;
        try { Task.WaitAll(copies.Select(c => (Task)c.Process.Exited).ToArray(), TimeSpan.FromSeconds(2)); }
        catch (Exception) { /* a faulted wait changes nothing */ }
        foreach (var copy in copies)
            if (copy.Alive) copy.Process.Kill();
    }

    /// <summary>
    /// Hooks this game's end once, three ways, since any one of them may not fire: the game's
    /// own Quit (the one door for the close button, Cmd+Q and the menu's Quit), the scene
    /// tree's root leaving the tree, and the process exiting. Main thread. Only ever called
    /// right before a copy starts, so a solo game is never patched.
    /// </summary>
    private static void HookExit()
    {
        if (Interlocked.Exchange(ref _exitHooked, 1) != 0) return;
        try
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                _quiet = true; // the engine may be gone by now, so nothing is printed
                try { EndCopies(wait: true); }
                catch (Exception) { /* nothing may be thrown into the runtime's shutdown */ }
            };
            if (Engine.GetMainLoop() is SceneTree { Root: { } root })
                root.Connect(Node.SignalName.TreeExiting, Callable.From(() =>
                {
                    try { EndCopies(wait: false); }
                    catch (Exception ex) { Log($"coop: ending the bot copies as the scene tree closed failed: {ex.Message}", error: true); }
                }));
        }
        catch (Exception ex)
        {
            Log($"coop: couldn't hook this game's exit; bot copies end themselves a few seconds after it is gone: {ex.Message}", error: true);
        }
        PatchQuit();
    }

    /// <summary>
    /// A Harmony prefix on NGame.Quit(), which only ever runs our own <see cref="BeforeQuit"/>
    /// first and then lets the game quit as always. In the human's game it is applied with the
    /// first copy it starts, in a bot copy at mod load.
    /// </summary>
    private static void PatchQuit()
    {
        if (Interlocked.Exchange(ref _quitPatched, 1) != 0) return;
        try
        {
            var quit = HarmonyLib.AccessTools.Method(typeof(NGame), nameof(NGame.Quit), Type.EmptyTypes);
            var prefix = typeof(CoopBots).GetMethod(nameof(BeforeQuit), BindingFlags.NonPublic | BindingFlags.Static);
            if (quit is null || prefix is null)
            {
                Log("coop: NGame.Quit() was not found, so quitting is not hooked (after a game update, check it again)", error: true);
                return;
            }
            new HarmonyLib.Harmony("mcpcodes.spiremind.coopbots").Patch(quit, prefix: new HarmonyLib.HarmonyMethod(prefix));
            Log("coop: hooked the game's Quit");
        }
        catch (Exception ex)
        {
            Log($"coop: hooking the game's Quit failed: {ex.Message}", error: true);
        }
    }

    /// <summary>
    /// Runs right before the game's own Quit, on the main thread. Host: the copies are asked to
    /// go (a signal each; nothing waits). Bot copy: the goodbye is sent first, so closing a
    /// bot's window or Cmd+Q in it leaves no ghost on the host. Only the network is touched
    /// here, never the scene tree: this can run while the engine hands the close request down
    /// the tree. Never throws into the game.
    /// </summary>
    private static void BeforeQuit()
    {
        try
        {
            if (IsBotCopy) Log($"coop: quitting: {SayGoodbye()}");
            else EndCopies(wait: false);
        }
        catch (Exception ex)
        {
            Log($"coop: the quit hook failed: {ex.Message}", error: true);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Finding copies
    // ---------------------------------------------------------------------------------------

    /// <summary>GET /health on a local port: true when a SpireMind bridge answers.</summary>
    private static async Task<bool> ProbeAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            using var response = await Http.Value.GetAsync($"http://127.0.0.1:{port}/health", limit.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            return body.Contains("spiremind", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return false; } // refused, timed out, not HTTP
    }

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>A game process started without Steam that joins: a bot copy, whoever started it.</summary>
    private sealed record Running(int Pid, ulong ClientId);

    /// <summary>
    /// The bot copies running on this Mac, from the process list (ps), so one whose bridge
    /// never came up, or one started by scripts/coop-bot.sh, is found too. Ok is false when the
    /// list could not be read; the checks that lean on it then say less instead of guessing.
    /// </summary>
    private static async Task<(bool Ok, List<Running> Copies)> ScanAsync(CancellationToken ct)
    {
        var found = new List<Running>();
        try
        {
            var exe = System.Environment.ProcessPath ?? "";
            using var ps = ProcessLauncher.Instance.Start(new ChildProcessSpec("/bin/ps", ["-axww", "-o", "pid=,command="], null, new Dictionary<string, string?>()));
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(4));
            await foreach (var raw in ps.StandardOutput.ReadAllAsync(limit.Token).ConfigureAwait(false))
            {
                var line = raw.TrimStart();
                var space = line.IndexOf(' ');
                if (space <= 0 || !int.TryParse(line[..space], out var pid) || pid == System.Environment.ProcessId) continue;
                var command = line[(space + 1)..];
                var isGame = (exe.Length > 0 && command.StartsWith(exe, StringComparison.Ordinal))
                             || command.Contains("/SlayTheSpire2.app/Contents/MacOS/", StringComparison.Ordinal);
                if (!isGame || !NoSteamArg.IsMatch(command) || HostArg.IsMatch(command)) continue;
                // Its id is --clientId, or 1000 without one, as the game's join has it.
                var clientId = ClientArg.Match(command) is { Success: true } c && ulong.TryParse(c.Groups[1].Value, out var id) ? id : BotArgs.DefaultNetId;
                found.Add(new Running(pid, clientId));
            }
            return (true, found);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log($"coop: the process list could not be read ({ex.Message}); going by bridges and ports only", error: true);
            return (false, found);
        }
    }

    private static bool PortFree(int port)
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------------------------------
    // The frame tick
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Main thread, in the human's game, right before its first copy starts: the exit hooks
    /// that end the copies with this game, and the frame tick for the focus watch. A solo game
    /// never gets here.
    /// </summary>
    private static bool HookHost()
    {
        HookExit();
        // Only the focus watch needs the tick: without it a copy still starts and is still ended with this game.
        try
        {
            if (!HookTick()) Log("coop: the frame tick could not be hooked, so the keyboard focus is not watched after a start", error: true);
        }
        catch (Exception ex)
        {
            Log($"coop: the frame tick could not be hooked, so the keyboard focus is not watched after a start: {ex.Message}", error: true);
        }
        return true;
    }

    /// <summary>Makes <see cref="Tick"/> run every frame: connects it to SceneTree.ProcessFrame once. Main thread. False when there is no scene tree to connect to (it is tried again at the next call).</summary>
    private static bool HookTick()
    {
        if (Interlocked.Exchange(ref _hooked, 1) != 0) return true;
        try
        {
            if (Engine.GetMainLoop() is SceneTree tree)
            {
                tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(Tick));
                return true;
            }
        }
        catch (Exception)
        {
            Volatile.Write(ref _hooked, 0);
            throw;
        }
        Volatile.Write(ref _hooked, 0);
        return false;
    }

    /// <summary>
    /// The per-frame work, on the main thread: a bot copy's window, and on the host the short
    /// watch over the keyboard focus after a start. Each part fails on its own: it is logged
    /// once and switched off, and never reaches the game.
    /// </summary>
    private static void Tick()
    {
        // Never twice in one frame, whoever calls.
        ulong frame;
        try { frame = Engine.GetProcessFrames(); }
        catch { return; }
        if (frame == _lastFrame) return;
        _lastFrame = frame;

        if (IsBotCopy)
        {
            BotWindow.Tick(); // guards itself
            return;
        }
        if (_hostBroken || _focusUntil == 0) return;
        try { WatchFocus(System.Environment.TickCount64); }
        catch (Exception ex)
        {
            _hostBroken = true;
            Log($"coop: the watch over the keyboard focus failed and is off (copies can still be started and stopped): {ex}", error: true);
        }
    }

    private static void ArmFocusWatch(bool hadFocus)
    {
        if (!hadFocus)
        {
            // Asked from outside the window (the bridge's /chat), or the human had already switched away: nothing to take back.
            _focusUntil = 0;
            Log("coop: focus: this window did not have the keyboard when the copy was started, so it is not watched");
            return;
        }
        var now = System.Environment.TickCount64;
        _focusFrom = now;
        _focusUntil = now + FocusWatchMs;
        _hadFocus = hadFocus;
        _unfocusedSince = 0;
        _lastRetake = 0;
        _retakes = 0;
    }

    /// <summary>
    /// For a quarter of a minute after a copy starts: the engine asks macOS to activate every
    /// new copy when its window appears, and the copy can't prevent that from inside. If this
    /// window had the keyboard and loses it then, it takes it back, at most three times, and
    /// logs each reading, which answers whether a starting copy steals focus at all. A human
    /// who switches away on purpose in those seconds is pulled back; after them, never.
    /// Host only: a bot copy never comes forward.
    /// </summary>
    private static void WatchFocus(long now)
    {
        if (_focusUntil == 0) return;
        if (now > _focusUntil)
        {
            if (_unfocusedSince != 0) Log("coop: focus: this window still does not have the keyboard 15 s after a copy started", error: true);
            _focusUntil = 0;
            return;
        }

        if (DisplayServer.WindowIsFocused())
        {
            if (_unfocusedSince != 0) Log($"coop: focus: this window has the keyboard again after {now - _unfocusedSince} ms");
            _unfocusedSince = 0;
            return;
        }
        if (_unfocusedSince == 0)
        {
            _unfocusedSince = now;
            Log($"coop: focus: this window lost the keyboard {now - _focusFrom} ms after a copy started");
        }
        if (!_hadFocus || _retakes >= 3 || now - _unfocusedSince < 500 || now - _lastRetake < 1000) return;
        _retakes++;
        _lastRetake = now;
        Log($"coop: focus: taking the keyboard back (try {_retakes})");
        DisplayServer.WindowMoveToForeground();
    }

    // ---------------------------------------------------------------------------------------
    // Quitting: bot side
    // ---------------------------------------------------------------------------------------

    /// <summary>Once, in a bot copy: take SIGTERM as a request to leave and quit, watch for the host's game to disappear, and make every quit say goodbye.</summary>
    private static void BotSetup()
    {
        try
        {
            _sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true; // not the default (die at once, leaving a ghost on the host)
                RequestQuit("SIGTERM");
            });
            Log("coop: SIGTERM now means: leave the lobby or run, then quit");
        }
        catch (Exception ex)
        {
            Log($"coop: couldn't take over SIGTERM ({ex.Message}); a terminated copy leaves a ghost on the host", error: true);
        }

        if (BotArgs.HostPid is { } hostPid)
        {
            try { _parentAtStart = SysGetParent(); }
            catch (Exception) { _parentAtStart = 0; }
            _hostWatch = new System.Threading.Timer(_ => CheckHost(hostPid), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
            Log($"coop: watching the game that started this copy (pid {hostPid}); this copy quits when it is gone");
        }
        else
        {
            Log($"coop: no --{BotArgs.HostPidFlag}, so this copy does not quit by itself when the host's game goes away");
        }

        PatchQuit();
    }

    /// <summary>On a timer thread. Two misses in a row, so one odd reading never ends a session.</summary>
    private static void CheckHost(int hostPid)
    {
        try
        {
            // A parent that exits hands its children to launchd at once, even while it still shows in the process list.
            var orphaned = false;
            if (_parentAtStart == hostPid)
            {
                try { orphaned = SysGetParent() != hostPid; }
                catch (Exception) { /* no libc: the process check below still works */ }
            }
            if (!orphaned && ProcessExists(hostPid))
            {
                _hostMissing = 0;
                return;
            }
            if (++_hostMissing < 2) return;
            _hostWatch?.Dispose();
            RequestQuit($"the game that started this copy (pid {hostPid}) is gone");
        }
        catch (Exception ex)
        {
            // Said once; the watch itself goes on, since it is the only guard against a host that crashed.
            if (Interlocked.Exchange(ref _hostWatchFailed, 1) == 0)
                Log($"coop: the host watch failed (it keeps trying, silently): {ex.Message}", error: true);
        }
    }

    private static async Task QuitAsync()
    {
        try
        {
            var did = await ModEntry.MainThread.Run(() =>
            {
                // No new join from here on: the watcher stops, and the main menu that leaving
                // a run builds does not dial the host again while this copy is on its way out.
                JoinWatcher.Halt();
                return LeaveSession();
            }).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            Log($"coop: before quitting: {did}");
        }
        catch (Exception ex)
        {
            Log($"coop: leaving the session failed ({ex.Message}); quitting anyway, which leaves a ghost on the host", error: true);
        }

        // The goodbye is flushed inside the leave. Quit a few frames later (a join attempt that
        // was cancelled is picked up by tasks that continue on a later frame), but soon: after
        // leaving a run the game returns to a new main menu, which with --fastmp=join joins again.
        await Task.Delay(250).ConfigureAwait(false);
        try
        {
            await ModEntry.MainThread.Run(() =>
            {
                if (NGame.Instance is { } game && GodotObject.IsInstanceValid(game)) game.Quit(); // saves settings and progress, then quits
                else (Engine.GetMainLoop() as SceneTree)?.Quit();
            }).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"coop: the game's quit failed: {ex.Message}", error: true);
        }

        // A copy whose main thread no longer runs frames can't quit that way, and must not linger.
        await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        Log("coop: the game did not quit when asked; exiting the process", error: true);
        // Exit runs the mod's own shutdown first (its child processes end with it), then the
        // engine's, which may wait for ever on the stuck main thread. So a shell of its own ends
        // this process five seconds on: it depends on nothing in here still running.
        try
        {
            var me = System.Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ProcessLauncher.Instance.Start(new ChildProcessSpec("/bin/sh", ["-c", $"sleep 5; kill -{SigKill} {me}"], null, new Dictionary<string, string?>()));
        }
        catch (Exception) { /* no shell to be had: the exit below is all there is */ }
        System.Environment.Exit(0);
    }

    /// <summary>
    /// Leaves the lobby or the run as the game's own menus do, so the host gets the goodbye
    /// packet and removes this player. Main thread, bot copy (a client) only: on a host the
    /// same calls would close the lobby or end the session for everyone, so this is never
    /// reached there (<see cref="RequestQuit"/> refuses).
    /// </summary>
    private static string LeaveSession()
    {
        if (!IsBotCopy) return "not a bot copy; nothing done";
        var sight = Look();
        var net = sight.Net;
        if (sight.Place == Place.Run)
        {
            if (net is not { IsConnected: true, Type: NetGameType.Client }) return "in a run, not connected as a client; nothing to leave";
            // What the pause menu's Disconnect does after its confirmation.
            net.Disconnect(NetError.Quit);
            return "left the run (goodbye sent)";
        }

        var stack = NGame.Instance?.MainMenu?.SubmenuStack;
        if (stack is null) return "no menu yet; nothing to leave";
        switch (sight.Place)
        {
            case Place.Lobby or Place.LoadedLobby when net is { IsConnected: true, Type: NetGameType.Client }:
                // Closing the lobby screen cleans the lobby up and disconnects with the goodbye.
                // While the run is being started the screen must stay; then only the connection is closed.
                if (NGame.Instance!.Transition?.InTransition == true)
                {
                    net.Disconnect(NetError.Quit);
                    return "left the lobby while the run was starting (goodbye sent)";
                }
                stack.Pop();
                return "left the lobby (goodbye sent)";
            case Place.Menu when sight.Screen is NJoinFriendScreen:
                // Closing the join screen cancels an attempt in flight, which says goodbye if it had connected.
                stack.Pop();
                return "closed the join screen";
            default:
                return "not in a lobby or run; nothing to leave";
        }
    }

    /// <summary>
    /// The goodbye alone, for the quit hook: closes this client's connection to the lobby or
    /// run with reason Quit, which sends the packet that makes the host drop this player. No
    /// screen is closed. Idempotent: a client that already left is not connected. Main thread,
    /// bot copy only.
    /// </summary>
    private static string SayGoodbye()
    {
        if (!IsBotCopy) return "not a bot copy; nothing done";
        var net = Look().Net;
        if (net is not { IsConnected: true, Type: NetGameType.Client }) return "not connected to a lobby or run; nothing to send";
        net.Disconnect(NetError.Quit);
        return "goodbye sent to the host";
    }

    // ---------------------------------------------------------------------------------------
    // Small things
    // ---------------------------------------------------------------------------------------

    private static async Task<T> OnMain<T>(Func<T> read, CancellationToken ct) =>
        await ModEntry.MainThread.Run(read).WaitAsync(MainThreadWait, ct).ConfigureAwait(false);

    /// <summary>The copy's game log once the game has started it, else what it printed before that.</summary>
    private static string LogOf(Copy copy)
    {
        try { return new FileInfo(copy.LogPath) is { Exists: true, Length: > 0 } ? copy.LogPath : copy.ConsolePath; }
        catch (Exception) { return copy.LogPath; }
    }

    private static List<string> Tail(Copy copy, int lines)
    {
        try
        {
            var path = LogOf(copy);
            if (!File.Exists(path)) return [];
            using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(Math.Max(0, stream.Length - 8192), SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var all = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return all.Skip(Math.Max(0, all.Length - lines)).Select(l => l.Length > 240 ? l[..240] : l).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The whole lines added to a file another process is writing, since <paramref name="offset"/>.</summary>
    private static List<string> NewLines(string path, ref long offset, ref string carry)
    {
        var lines = new List<string>();
        try
        {
            if (!File.Exists(path)) return lines;
            using var stream = new FileStream(path, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < offset) (offset, carry) = (0, ""); // another file now (the game's log took over from the console file)
            stream.Seek(offset, SeekOrigin.Begin);
            var buffer = new byte[(int)Math.Min(1 << 20, stream.Length - offset)];
            var read = stream.Read(buffer, 0, buffer.Length);
            offset += read;
            var text = carry + Encoding.UTF8.GetString(buffer, 0, read);
            var end = text.LastIndexOf('\n');
            if (end < 0)
            {
                carry = text.Length > 4096 ? "" : text;
                return lines;
            }
            carry = text[(end + 1)..];
            lines.AddRange(text[..end].Split('\n', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Try again at the next look.
        }
        return lines;
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Sentence(string clause) => Capital(clause) + ".";

    /// <summary>Once the process is exiting the engine may be gone, so nothing is printed any more.</summary>
    private static void Log(string message, bool error = false)
    {
        if (_quiet) return;
        try
        {
            if (error) ModEntry.LogError(message);
            else ModEntry.Log(message);
        }
        catch (Exception) { /* a log line must never break a caller */ }
    }

    private static bool Signal(int pid, int signal)
    {
        try { return SysKill(pid, signal) == 0; }
        catch (Exception) { return false; } // no libc to call from here
    }

    /// <summary>Whether a process with this id exists (signal 0 only asks).</summary>
    private static bool ProcessExists(int pid)
    {
        try
        {
            const int NoSuchProcess = 3; // ESRCH
            return SysKill(pid, 0) == 0 || Marshal.GetLastPInvokeError() != NoSuchProcess;
        }
        catch (Exception)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (Exception) { return true; } // unknown counts as alive: never quit a copy on a guess
        }
    }

    // kill(2) and getppid(2) from libc, as Core's ChildProcess reaches kill.
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int SysKill(int pid, int signal);

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int SysGetParent();
}
