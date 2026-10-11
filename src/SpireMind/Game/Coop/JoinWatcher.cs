using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Timeline.Epochs;
// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using InstanceRole = SpireMind.Core.Brain.InstanceRole;

namespace SpireMind.Game.Coop;

/// <summary>Where a bot copy stands between its main menu and a co-op run.</summary>
internal enum CoopPlace
{
    /// <summary>No main menu and no run scene: the game is starting, or one scene is replacing another.</summary>
    Between,

    /// <summary>On the main menu, with no join attempt running and no lobby.</summary>
    Menu,

    /// <summary>A join attempt is running: the Join screen's loading overlay is up.</summary>
    Joining,

    /// <summary>In a new run's lobby as a client: character select.</summary>
    Lobby,

    /// <summary>In the lobby of a saved co-op run.</summary>
    LoadedLobby,

    /// <summary>A lobby screen the driver does not play: custom or daily, one this copy hosts, or one whose lobby is gone.</summary>
    OtherLobby,

    /// <summary>A run exists while the main menu is still the scene: the start, or a start that failed.</summary>
    RunStarting,

    /// <summary>The run scene is up.</summary>
    Run,
}

/// <summary>One look at the menu, taken on the main thread. Nothing in it may be kept across steps.</summary>
internal readonly record struct CoopView(
    CoopPlace Place,
    NMainMenu? Menu,
    NMainMenuSubmenuStack? Stack,
    NSubmenu? Top,
    StartRunLobby? Lobby,
    bool AttemptRunning);

/// <summary>
/// Gets a bot copy into the host's lobby and keeps it trying. The game makes one join attempt
/// each time its Join screen opens (with <c>--fastmp join</c>, once for every new main menu) and
/// never retries, so this watches that attempt, reads why it failed, closes the error popup the
/// safe way, and opens the Join screen again after a pause that grows. It also prepares the
/// bot's own profile before the first join, since the join request carries the ascension cap
/// and the unlocks.
///
/// Bot copies only: every entry point returns at once unless <see cref="IsBotCopy"/>, which
/// needs the role the mod worked out at load (<see cref="InstanceRole"/>, from --sts2bot) to
/// say so. A bot copy is always a CLIENT. Nothing here may run in the host's copy: opening the
/// Join screen there would dial 127.0.0.1, and the profile changes would rewrite Mark's
/// progress. The four Harmony patches are applied by hand, in a bot copy only, so the human's
/// copy and a solo game run the game's own code untouched.
///
/// Runs on the main thread from the frame tick (<see cref="LobbyDriver.Tick"/> calls
/// <see cref="Tick"/>), one small step every <see cref="StepEvery"/>. <see cref="Status"/>,
/// <see cref="Stopped"/> and <see cref="RetryNow"/> are safe from any thread. Every line it
/// logs starts with "coop: join:" or "coop: profile:".
/// </summary>
internal static class JoinWatcher
{
    private const string HarmonyId = "mcpcodes.spiremind.coop";

    /// <summary>The highest multiplayer ascension the game knows (it clamps a saved value to this).</summary>
    private const int TopAscension = 10;

    private const string AscensionNotice = "ascension_multiplayer_ftue";

    // ---------- timing ----------

    private static readonly TimeSpan StepEvery = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Two stages of a join have no timeout on the client. An attempt older than this is closed.
    /// Never sooner than 25 s: a cancel in the first seconds can leave the host holding our id.
    /// </summary>
    private static readonly TimeSpan WatchdogAfter = TimeSpan.FromSeconds(45);

    /// <summary>
    /// After closing a Join screen whose attempt was still running, its clean-up may hide the
    /// loading overlay a frame late. A new attempt started in between would read as idle.
    /// </summary>
    private static readonly TimeSpan GapAfterCancel = TimeSpan.FromSeconds(1.5);

    /// <summary>A screen the bot didn't open (Timeline after a run, settings) is left alone this long before it is closed.</summary>
    private static readonly TimeSpan StrayPatience = TimeSpan.FromSeconds(5);

    /// <summary>After leaving a lobby with no reason given (we left, or the popup never came): a short breath.</summary>
    private static readonly TimeSpan AfterLobby = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The host still counts as "in a run" while it sits on its game over screen, and answers a
    /// join by re-admitting us and losing us again. So after a run the first try waits this
    /// long, and the new main menu's own attempt is held back (<see cref="BeforeMenuCommandLine"/>).
    /// </summary>
    private static readonly TimeSpan AfterRun = TimeSpan.FromSeconds(30);

    /// <summary>A failure reported this soon after the run scene went away belongs to that run.</summary>
    private static readonly TimeSpan RunIsFresh = TimeSpan.FromSeconds(20);

    /// <summary>A popup that opens this soon after a recorded failure is that failure's own popup.</summary>
    private static readonly TimeSpan RecordIsFresh = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan FaultPause = TimeSpan.FromSeconds(2);

    private const int MaxFaults = 5;

    private const int MaxFaultsLogged = 6;

    // ---------- state (main thread) ----------

    private static bool _started;
    private static bool _disabled;
    private static bool _patched;
    private static bool _steamNoted;
    private static int _faults;
    private static int _faultsLogged;
    private static ulong _lastFrame = ulong.MaxValue;
    private static DateTime _nextStep;

    /// <summary>The role the mod worked out at load; null until <see cref="Start"/>, and for good in a copy that is not a bot.</summary>
    private static InstanceRole? _role;

    /// <summary>When the watcher last saw a run (starting or running), for telling what a failure interrupted.</summary>
    private static DateTime _runSeenAt;

    /// <summary>The next try waits for the host to leave its game over screen, which the status line says.</summary>
    private static bool _waitIsAfterRun;

    /// <summary>The place seen on the last step: what a failure recorded since then interrupted.</summary>
    private static CoopPlace _place = CoopPlace.Between;

    private static ulong _menuId;

    /// <summary>The main menu whose Join screen has been pushed at least once, so its loading overlay can be trusted.</summary>
    private static ulong _joinSeenMenu;

    /// <summary>The main menu whose own <c>--fastmp join</c> was held back, so it never pushed its Join screen by itself.</summary>
    private static ulong _heldMenu;

    /// <summary>When the running attempt was first seen (the game starts its own before our first step).</summary>
    private static DateTime? _attemptSince;

    private static bool _attemptHadReason;

    /// <summary>Attempts since the last lobby.</summary>
    private static int _attempts;

    /// <summary>Failures in a row, of any kind, and with the same reason.</summary>
    private static int _failures;
    private static int _sameReason;
    private static NetError? _lastReason;

    private static DateTime _nextAttemptAt;
    private static bool _stopped;
    private static string _stopWhy = "";
    private static string _lastFailure = "";

    private static ulong _strayId;
    private static DateTime _straySince;
    private static string? _blockedBy;

    /// <summary>The progress object the profile was prepared on; the game swaps in another when the save loads.</summary>
    private static object? _prepared;

    /// <summary>Failures the popup patch saw since the last step, oldest first. Locked: the patch may run off the main thread.</summary>
    private static readonly Queue<Failure> Recorded = new();

    /// <summary>The error popup whose failure has been counted, so it is counted once while it waits to be closed.</summary>
    private static ulong _notedPopup;
    private static DateTime _recordedTakenAt;

    private static bool _patchFailureLogged;
    private static bool _holdFailureLogged;
    private static bool _timelineFailureLogged;

    private static volatile string _status = "not started";
    private static volatile bool _retryNow;

    /// <summary>
    /// Steam was up in this process at some point. From then on the copy is never a bot copy,
    /// also when Steam goes away later: the game clears its flag when Steam quits under it, and
    /// the progress it holds then is still the human's.
    /// </summary>
    private static volatile bool _steamSeen;

    /// <summary>The copy is quitting: nothing may open the Join screen any more, a new main menu's own dispatch included.</summary>
    private static volatile bool _halted;

    private sealed record Failure(NetError? Reason, string Detail, CoopPlace During);

    // ---------- for the other parts ----------

    /// <summary>One line for a human: where the join stands. Safe from any thread.</summary>
    public static string Status => _status;

    /// <summary>True once a failure that retrying cannot fix stopped the attempts (version or mod mismatch, repeated internal errors).</summary>
    public static bool Stopped => _stopped;

    /// <summary>Where this copy stood at the watcher's last step. Main thread.</summary>
    internal static CoopPlace Place => _place;

    /// <summary>
    /// Off by default: the bot's unlocks stay as its profile has them. The launcher writes a new
    /// bot profile with the epochs the human has revealed (BotProfile), so the bot can pick the
    /// characters he can, and a run's shared content (the union of every player's unlocks) holds
    /// nothing he has not unlocked himself. Set true before <see cref="LobbyDriver.Start"/> to
    /// reveal the four character epochs on the bot's own profile whatever the human has (the
    /// Ironclad needs none), as the game's own AutoSlay runner does: every character can then
    /// be picked from the bot's next main menu on.
    /// </summary>
    public static bool UnlockCharacters { get; set; }

    /// <summary>
    /// Stops the watcher for good, for a copy that is about to quit: no more steps, and a main
    /// menu built from now on (leaving a run builds one) does not start its own join either.
    /// Main thread.
    /// </summary>
    internal static void Halt()
    {
        _halted = true;
        _disabled = true;
        _status = "this copy is quitting";
    }

    /// <summary>
    /// Try again now, also after a stop: for the day the host's copy tells this one that its
    /// lobby is open. Safe from any thread; acted on at the next step.
    /// </summary>
    public static void RetryNow() => _retryNow = true;

    /// <summary>
    /// True in a copy the mod's role says is a bot (<see cref="InstanceRole.IsBot"/>, from
    /// --sts2bot) and that runs without Steam. The role decides; the Steam test is a second
    /// lock on top of it: the profile writes below must never reach the human's progress,
    /// whatever a command line says. The game brings Steam up (or skips it) before mods load,
    /// so <see cref="Start"/> already knows; it is still asked at every step and inside every
    /// patch, and "Steam was up" is kept for the life of the process: the game sets its flag
    /// back to false when Steam quits under it, and that must not turn the human's copy into
    /// a bot copy half way through a session.
    /// </summary>
    internal static bool IsBotCopy()
    {
        if (_role is not { IsBot: true } || _steamSeen) return false;
        if (!SteamInitializer.Initialized) return true;
        _steamSeen = true;
        return false;
    }

    /// <summary>
    /// Call once at mod load, on the main thread, with the role the mod detected
    /// (<see cref="LobbyDriver.Start"/> does). In a bot copy it patches four game methods: the
    /// error popup's factory (read-only: why a join failed), the main menu's _Ready (prepares
    /// the profile before the menu's own join), the menu's command-line dispatch (holds back
    /// the automatic join of a menu built right after a run) and the game over screen's detour
    /// to the Timeline (a bot goes straight to the menu). In any other copy, and in one started
    /// as a bot but with Steam running, it patches nothing and <see cref="Tick"/> never touches
    /// the game.
    /// </summary>
    public static void Start(InstanceRole role)
    {
        if (_started) return;
        _started = true;
        try
        {
            if (role is not { IsBot: true })
            {
                _status = "not a bot copy";
                return;
            }

            _role = role;

            // Steam is settled by now (the game brings it up before it loads mods). With Steam
            // this copy plays from the Steam profile: nothing is patched and nothing ever runs.
            if (!IsBotCopy())
            {
                NoteSteam();
                return;
            }

            ModEntry.Log($"coop: join: this is bot copy {role.InstanceId} (client id {CommandLineHelper.GetValue("clientId") ?? "1000, the game's default"}, " +
                         $"fastmp {CommandLineHelper.GetValue("fastmp") ?? "not given"}); looking for a lobby on this Mac");
            Patch();
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: join: start failed: {ex.Message}");
        }
    }

    /// <summary>Call every frame on the main thread. A second call in the same frame does nothing.</summary>
    public static void Tick()
    {
        if (_role is null || _disabled) return;
        var frame = Engine.GetProcessFrames();
        if (frame == _lastFrame) return;
        _lastFrame = frame;

        var now = DateTime.UtcNow;
        if (now < _nextStep) return;
        _nextStep = now + StepEvery;

        try
        {
            if (!IsBotCopy())
            {
                // The role says bot, but Steam is up: this copy plays from the Steam profile.
                NoteSteam();
                return;
            }
            Step(now);
            _faults = 0;
        }
        catch (Exception ex)
        {
            Fault(now, ex);
        }
    }

    /// <summary>Says once that a copy started as a bot runs with Steam and is therefore left alone.</summary>
    private static void NoteSteam()
    {
        if (_steamNoted) return;
        _steamNoted = true;
        _status = "started as a bot copy, but with Steam running; it does not join by itself";
        ModEntry.LogError("coop: join: this copy was started with --sts2bot but Steam is running in it, so it is left alone: " +
                          "it does not join, pick or ready by itself, and its profile is not touched. Start bot copies with --force-steam off.");
    }

    /// <summary>
    /// A step that throws is logged (the first few times ever) and tried again after a pause.
    /// <see cref="MaxFaults"/> in a row and the watcher switches itself off: it must never take
    /// the game down with it.
    /// </summary>
    private static void Fault(DateTime now, Exception ex)
    {
        _faults++;
        _nextStep = now + FaultPause;
        if (_faultsLogged < MaxFaultsLogged)
        {
            _faultsLogged++;
            ModEntry.LogError($"coop: join: step failed ({_faults} in a row): {ex}");
        }
        if (_faults < MaxFaults) return;

        _disabled = true;
        _status = "the join watcher stopped after repeated errors (see the log)";
        ModEntry.LogError("coop: join: switched off after repeated errors; this copy no longer joins by itself");
    }

    // ---------- reading where we are ----------

    /// <summary>
    /// Reads where this copy is. Main thread only. Safe in any copy (it only reads), though only
    /// a bot copy calls it.
    /// </summary>
    internal static CoopView Look()
    {
        var game = NGame.Instance;
        if (game is null || !GodotObject.IsInstanceValid(game) || game.RootSceneContainer is null)
            return default;

        var rm = RunManager.Instance;
        var menu = game.MainMenu;
        if (menu is null || !GodotObject.IsInstanceValid(menu))
            return new CoopView(rm.IsInProgress && game.CurrentRunNode is not null ? CoopPlace.Run : CoopPlace.Between, null, null, null, null, false);

        // Assigned in the menu's _Ready, so null for a moment after the scene is set.
        var stack = menu.SubmenuStack;
        if (stack is null || !GodotObject.IsInstanceValid(stack))
            return new CoopView(CoopPlace.Between, menu, null, null, null, false);

        var top = stack.Peek();

        // The main menu stays the scene while a run is set up: from here the lobby is no longer ours to touch.
        if (rm.IsInProgress)
            return new CoopView(CoopPlace.RunStarting, menu, stack, top, null, false);

        switch (top)
        {
            case NCharacterSelectScreen select:
                // Lobby is null once the screen left its lobby (a start that failed leaves it so).
                var lobby = select.Lobby;
                return lobby is not null && lobby.NetService.Type == NetGameType.Client
                    ? new CoopView(CoopPlace.Lobby, menu, stack, top, lobby, false)
                    : new CoopView(CoopPlace.OtherLobby, menu, stack, top, null, false);

            case NMultiplayerLoadGameScreen:
                return new CoopView(CoopPlace.LoadedLobby, menu, stack, top, null, false);

            case NCustomRunScreen or NDailyRunScreen or NCustomRunLoadScreen or NDailyRunLoadScreen:
                return new CoopView(CoopPlace.OtherLobby, menu, stack, top, null, false);

            case NJoinFriendScreen join:
                _joinSeenMenu = menu.GetInstanceId();
                return OverlayUp(join)
                    ? new CoopView(CoopPlace.Joining, menu, stack, top, null, true)
                    : new CoopView(CoopPlace.Menu, menu, stack, top, null, false);

            // The Join screen is only ever pushed from the multiplayer submenu, so with that on top
            // (or nothing open) it is not in the stack and no attempt can be running.
            case null or NMultiplayerSubmenu:
                return new CoopView(CoopPlace.Menu, menu, stack, top, null, false);

            default:
                // Something covers the stack (the Timeline after a run). An attempt may be running
                // underneath. A Join screen that was never pushed in this menu shows whatever its
                // scene file has, so only one that was pushed is read.
                var covered = JoinPushedIn(menu) && OverlayUp(stack.GetSubmenuType<NJoinFriendScreen>());
                return new CoopView(covered ? CoopPlace.Joining : CoopPlace.Menu, menu, stack, top, null, covered);
        }
    }

    /// <summary>
    /// With <c>--fastmp join</c> a main menu pushes the Join screen from its _Ready, unless the
    /// watcher held that back (<see cref="BeforeMenuCommandLine"/>); otherwise we must have seen
    /// it on top.
    /// </summary>
    private static bool JoinPushedIn(NMainMenu menu)
    {
        var id = menu.GetInstanceId();
        return _joinSeenMenu == id || (_heldMenu != id && CommandLineHelper.GetValue("fastmp") == "join");
    }

    private static bool OverlayUp(NJoinFriendScreen? screen) =>
        Nodes.Alive(screen) && Nodes.Get<Godot.Control>(screen, "%LoadingOverlay") is { Visible: true };

    // ---------- the step ----------

    private static void Step(DateTime now)
    {
        // Until the profile id is set, Progress is a throw-away default the save load replaces.
        if (!SaveManager.Instance.IsProfileInitialized)
        {
            _status = "starting up";
            return;
        }

        // The fallback for the patch on the menu's _Ready: with --fastmp join the first join may
        // already be on its way by now, and then it carried the profile as it was.
        PrepareProfile(fromMenuPatch: false);

        if (_retryNow)
        {
            _retryNow = false;
            if (_stopped || now < _nextAttemptAt)
                ModEntry.Log("coop: join: asked to try again now");
            _stopped = false;
            _failures = 0;
            _sameReason = 0;
            _lastReason = null;
            _nextAttemptAt = now;
        }

        var view = Look();
        if (view.Place != _place && (Chatty(_attempts) || !Retrying(_place, view.Place)))
            ModEntry.Log($"coop: join: {Words(_place)} -> {Words(view.Place)}");

        if (view.Menu is { } menu && menu.GetInstanceId() != _menuId)
        {
            // A new main menu after every run and every return to it: nothing cached survives,
            // and with --fastmp join its own attempt is already running.
            _menuId = menu.GetInstanceId();
            _attemptSince = null;
            _strayId = 0;
        }

        // Failures first, wherever we are: _place still holds what they interrupted.
        var handled = TakeFailures(now);
        _place = view.Place;
        if (handled) return;

        switch (view.Place)
        {
            case CoopPlace.Lobby or CoopPlace.LoadedLobby:
                InLobby(now);
                return;

            case CoopPlace.OtherLobby:
                // A lobby of a kind the driver does not play counts as joined. A lobby screen
                // that lost its lobby (a start that failed) does not: the failures before it
                // still count towards giving up.
                if (_attemptSince is not null) InLobby(now);
                _nextAttemptAt = now + AfterLobby;
                _status = "on a lobby screen";
                return;

            case CoopPlace.RunStarting or CoopPlace.Run:
                _attemptSince = null;
                _runSeenAt = now;
                _nextAttemptAt = now + AfterRun;
                _waitIsAfterRun = true;
                _status = view.Place == CoopPlace.Run ? "in a run" : "the run is starting";
                return;

            case CoopPlace.Joining:
                WatchAttempt(now, view);
                return;

            case CoopPlace.Menu:
                OnMenu(now, view);
                return;

            default:
                _status = "between screens";
                return;
        }
    }

    private static void InLobby(DateTime now)
    {
        if (_attemptSince is { } since)
            ModEntry.Log($"coop: join: in a lobby after {(now - since).TotalSeconds:0} s (attempt {_attempts})");

        _attemptSince = null;
        _attempts = 0;
        _failures = 0;
        _sameReason = 0;
        _lastReason = null;
        _stopped = false;
        _blockedBy = null;
        _waitIsAfterRun = false;
        _nextAttemptAt = now + AfterLobby;
        _status = "in a lobby";
    }

    private static void WatchAttempt(DateTime now, CoopView view)
    {
        if (_attemptSince is null)
        {
            // Not started by us this step: the game's own attempt for this menu.
            BeginAttempt(now, "the game's own, from --fastmp join");
        }

        var age = now - _attemptSince!.Value;
        _status = $"joining the host's lobby (attempt {_attempts})";
        if (age < WatchdogAfter) return;

        // Only a Join screen on top can be closed: under another screen it is not ours to reach.
        if (view.Top is not NJoinFriendScreen || view.Stack is not { } stack || Nodes.ModalOpen()) return;

        // Closing the screen cancels its join (and sends the goodbye when it was connected). No popup follows.
        ModEntry.Log($"coop: join: attempt {_attempts} got no answer for {age.TotalSeconds:0} s; closing the Join screen to cancel it");
        stack.Pop();
        _attemptSince = null;
        _attemptHadReason = true;
        NoteFailure(now, new Failure(NetError.CancelledJoin, "", CoopPlace.Joining));
        if (_nextAttemptAt < now + GapAfterCancel) _nextAttemptAt = now + GapAfterCancel;
    }

    private static void OnMenu(DateTime now, CoopView view)
    {
        if (_attemptSince is not null)
        {
            // The overlay went down and no lobby followed. The popup, if one was built, was taken above.
            _attemptSince = null;
            if (!_attemptHadReason)
                NoteFailure(now, new Failure(null, "", CoopPlace.Joining));
        }

        if (_stopped)
        {
            _status = $"stopped: {_stopWhy}";
            return;
        }

        // An error popup is taken in TakeFailures and tutorial popups by the lobby driver. Any
        // other popup (mod consent, the Early Access notice) needs a person: the launcher's
        // seeded settings are meant to keep them away.
        if (NModalContainer.Instance?.OpenModal is Node modal && GodotObject.IsInstanceValid(modal))
        {
            var name = modal.GetType().Name;
            if (_blockedBy != name)
            {
                _blockedBy = name;
                ModEntry.Log($"coop: join: waiting: a popup is open in this copy's window ({name})");
            }
            _status = $"waiting: a popup is open in this copy's window ({name})";
            return;
        }
        _blockedBy = null;

        var wait = _nextAttemptAt - now;
        if (wait > TimeSpan.Zero)
        {
            _status = _failures > 0
                ? $"not in a lobby: {_lastFailure}; next try in about {Math.Ceiling(wait.TotalSeconds):0} s ({_failures} failed in a row)"
                : _waitIsAfterRun
                    ? $"back on the main menu after the run; looking for the host's next lobby in about {Math.Ceiling(wait.TotalSeconds):0} s"
                    : "not in a lobby; about to try";
            return;
        }
        _waitIsAfterRun = false;

        // A new menu fades in for two seconds; a fade that never ends is the lobby driver's to recover.
        if (NGame.Instance?.Transition?.InTransition == true)
        {
            _status = "waiting for the main menu";
            return;
        }

        if (view.Menu is { } menu && view.Stack is { } stack)
            OpenJoinScreen(now, menu, stack, view.Top);
    }

    /// <summary>
    /// One click's worth per step, as a person would: Back out of an idle Join screen, then Join
    /// again. Opening the Join screen is what starts an attempt (without Steam it dials
    /// 127.0.0.1:33771 with this copy's --clientId); calling the join itself as well would race
    /// a second attempt under the same id.
    /// </summary>
    private static void OpenJoinScreen(DateTime now, NMainMenu menu, NMainMenuSubmenuStack stack, NSubmenu? top)
    {
        switch (top)
        {
            case null:
                // Not through the menu's Multiplayer button: the menu disables it while the profile
                // holds an epoch not yet revealed on the Timeline, which a bot's does after a run.
                menu.OpenMultiplayerSubmenu().OnJoinFriendsPressed();
                BeginAttempt(now, "ours");
                return;

            case NMultiplayerSubmenu multiplayer:
                multiplayer.OnJoinFriendsPressed();
                BeginAttempt(now, "ours");
                return;

            case NJoinFriendScreen:
                // Idle: the last attempt failed. Its screen starts an attempt only when pushed.
                stack.Pop();
                return;

            default:
                CloseStray(now, stack, top);
                return;
        }
    }

    /// <summary>
    /// A screen this copy did not open itself sits on top: the Timeline (the game over screen's
    /// last button leads there when the profile discovered something), or one a person opened.
    /// Never a lobby screen: <see cref="Look"/> sends those elsewhere, and leaving a lobby is not
    /// this method's business.
    /// </summary>
    private static void CloseStray(DateTime now, NMainMenuSubmenuStack stack, NSubmenu top)
    {
        var name = top.GetType().Name;
        if (top.GetInstanceId() != _strayId)
        {
            _strayId = top.GetInstanceId();
            _straySince = now;
            ModEntry.Log($"coop: join: {name} is open over the menu; closing it in {StrayPatience.TotalSeconds:0} s if it stays");
        }

        _status = $"waiting: {name} is open in this copy's window";
        if (now - _straySince < StrayPatience) return;

        ModEntry.Log($"coop: join: closing {name}");
        _strayId = 0;
        stack.Pop();
    }

    private static void BeginAttempt(DateTime now, string whose)
    {
        _attemptSince = now;
        _attemptHadReason = false;
        // From here a failure is this attempt's own, however short ago the last run ended.
        _runSeenAt = default;
        _attempts++;
        if (Chatty(_attempts))
            ModEntry.Log($"coop: join: attempt {_attempts} ({whose})");
        _status = $"joining the host's lobby (attempt {_attempts})";
    }

    // ---------- failures ----------

    /// <summary>
    /// Takes what the popup patch recorded and closes an open error popup. True when it did
    /// something, so the rest of the step waits for the next one.
    ///
    /// The popup is closed with the modal container's Clear, which is what its own button ends
    /// with. Its button is never clicked: for several reasons the only button is "report a bug",
    /// which opens the feedback screen.
    /// </summary>
    private static bool TakeFailures(DateTime now)
    {
        var container = NModalContainer.Instance;
        var popup = container?.OpenModal as NErrorPopup;
        if (popup is not null && !GodotObject.IsInstanceValid(popup)) popup = null;

        var taken = 0;
        while (TryTakeRecorded(out var failure))
        {
            NoteFailure(now, failure);
            taken++;
        }
        if (taken > 0)
        {
            _recordedTakenAt = now;
            if (_attemptSince is not null) _attemptHadReason = true;
        }
        if (popup is null || container is null) return taken > 0;

        var id = popup.GetInstanceId();
        if (taken > 0)
        {
            _notedPopup = id;
        }
        else if (id != _notedPopup)
        {
            _notedPopup = id;
            // No reason on record for this popup: it was built without the patched factory (a run
            // start that threw, an unreadable save), or the patch did not apply. Its text is all
            // there is. A popup that follows a record by a moment is that record's own.
            if (now - _recordedTakenAt > RecordIsFresh)
            {
                var text = PopupText(popup);
                NoteFailure(now, new Failure(ReasonFromText(text), text, Interrupted()));
                if (_attemptSince is not null) _attemptHadReason = true;
            }
        }

        // The game adds a popup a frame late when it builds it off the main thread. Clearing
        // before that would miss the node and leave it on screen with no owner.
        if (!popup.IsInsideTree()) return true;

        container.Clear();
        // One line per popup would be one every few seconds for as long as nobody hosts.
        if (Chatty(_sameReason))
            ModEntry.Log("coop: join: closed the error popup (the modal container's Clear; its button is never clicked)");
        return true;
    }

    private static bool TryTakeRecorded([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Failure? failure)
    {
        lock (Recorded) return Recorded.TryDequeue(out failure);
    }

    /// <summary>
    /// What a failure reported now interrupted: the place of the last step, or the run when
    /// its scene went away a moment ago (a host who leaves a run sends the bot back to a new
    /// main menu first and shows the reason after).
    /// </summary>
    private static CoopPlace Interrupted()
    {
        var place = _place;
        if (place is (CoopPlace.Between or CoopPlace.Menu or CoopPlace.Joining) && DateTime.UtcNow - _runSeenAt < RunIsFresh)
            return CoopPlace.Run;
        return place;
    }

    private static void NoteFailure(DateTime now, Failure failure)
    {
        _failures++;
        _sameReason = failure.Reason == _lastReason && _failures > 1 ? _sameReason + 1 : 1;
        _lastReason = failure.Reason;
        _waitIsAfterRun = false;

        var wasIn = failure.During is CoopPlace.Lobby or CoopPlace.LoadedLobby or CoopPlace.OtherLobby or CoopPlace.Run or CoopPlace.RunStarting;
        var (pause, stop, why) = Policy(failure.Reason, _sameReason, _failures, wasIn);
        var name = failure.Reason?.ToString() ?? "no reason given";
        var detail = failure.Detail.Length > 0 && failure.Detail != name ? $": {OneLine(failure.Detail)}" : "";
        var where = failure.During switch
        {
            CoopPlace.Lobby or CoopPlace.LoadedLobby or CoopPlace.OtherLobby => "out of the lobby: ",
            CoopPlace.Run or CoopPlace.RunStarting => "out of the run: ",
            _ => "",
        };
        _lastFailure = $"{where}{why} ({name}{detail})";

        if (stop)
        {
            _stopped = true;
            _stopWhy = _lastFailure;
            _status = $"stopped: {_stopWhy}";
            ModEntry.LogError($"coop: join: STOPPED, retrying cannot fix this: {_stopWhy}");
            return;
        }

        _nextAttemptAt = now + pause;
        if (Chatty(_sameReason))
            ModEntry.Log($"coop: join: {_lastFailure}; next try in {pause.TotalSeconds:0} s ({_failures} failed in a row)");
    }

    /// <summary>
    /// How long to wait after each reason, and which reasons end the trying. "No host yet" is
    /// always Timeout (each such attempt already takes about 10 s), so that pause starts short
    /// and grows; every failed attempt leaves a socket for the garbage collector. A host in a run
    /// answers a join by building a rejoin answer and re-admitting us for a moment, so that one
    /// is asked at most once a minute.
    /// </summary>
    /// <param name="wasIn">The failure threw this copy out of a lobby or run it was in, rather
    /// than ending a join attempt.</param>
    private static (TimeSpan Pause, bool Stop, string Why) Policy(NetError? reason, int same, int failures, bool wasIn)
    {
        switch (reason)
        {
            case NetError.VersionMismatch:
                return (TimeSpan.Zero, true, "the host runs another game version, or a mod on one side changes the list of cards, relics or monsters; restart both copies on the same build with the same mods");
            case NetError.ModMismatch:
                return (TimeSpan.Zero, true, "the two copies load different gameplay mods; make them the same and restart the copy that changed");
            case NetError.InternalError:
                return same >= 3
                    ? (TimeSpan.Zero, true, "the game hit an internal error three times in a row; restart this copy")
                    : (Seconds(5 * same), false, "the game hit an internal error");
            case NetError.Timeout:
                return (Grow(3, failures), false, "nobody is hosting on this Mac yet");
            case NetError.HandshakeTimeout:
                return (Grow(3, failures), false, "the host gave up waiting for this copy's join request");
            case NetError.Quit:
                return (Grow(5, failures), false, "the host closed the lobby or left the run");
            case NetError.QuitGameOver:
                return (Grow(5, failures), false, "the host left after the run ended");
            case NetError.HostAbandoned:
                return (Grow(5, failures), false, "the host abandoned the run");
            case NetError.UnknownNetworkError:
                return (Grow(5, failures), false, "the connection to the host dropped");
            case NetError.CancelledJoin:
                return (Grow(5, failures), false, "the host did not answer the join");
            case NetError.RunInProgress:
                return (Seconds(60), false, "the host is in a run, and a running run cannot be joined");
            case NetError.NotInSaveGame:
                return (Seconds(60), false, "the host loaded a saved run that does not hold this client id");
            case NetError.StateDivergence:
                return (Seconds(60), false, "the host removed this copy because its fight differed from the host's");
            case NetError.Kicked when wasIn:
                // The game itself only answers a join with this (the id is taken). In a lobby or
                // run it is the host, through a mod's kick, sending this copy away: coming back
                // half a minute later would undo what the host just did.
                return (Seconds(300), false, "the host removed this copy; it stays away for five minutes");
            case NetError.Kicked:
                return same >= 3
                    ? (Seconds(60), false, "the host still holds a player with this client id, probably the ghost of an earlier copy; the host must close and reopen its lobby")
                    : (Seconds(30), false, "the host already holds a player with this client id");
            case NetError.LobbyFull:
                return (Seconds(30), false, "the lobby is full");
            case NetError.InvalidJoin:
                return (Seconds(30), false, "the host's lobby is of another kind than the join asked for");
            case null:
                return (Grow(5, failures), false, "the join ended without a reason the mod could read");
            default:
                return (Seconds(60), false, "the join was refused");
        }
    }

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    /// <summary>A pause that starts at <paramref name="first"/> seconds and grows by a third per failure up to half a minute.</summary>
    private static TimeSpan Grow(double first, int failures) =>
        Seconds(Math.Min(30, first * Math.Pow(1.3, Math.Clamp(failures - 1, 0, 20))));

    /// <summary>The first few of a kind are logged one by one, then every tenth: the trying goes on for as long as the copy runs.</summary>
    private static bool Chatty(int count) => count <= 3 || count % 10 == 0;

    private static string OneLine(string text)
    {
        var line = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 300 ? line : line[..297] + "...";
    }

    /// <summary>The popup's body, a private field set when it was built.</summary>
    private static string PopupText(NErrorPopup popup) =>
        Nodes.TryGetPrivate(popup, "_body") as string ?? "";

    /// <summary>The network error texts a client can be shown, by the table key the popup builds them from.</summary>
    private static readonly (NetError Reason, string Key)[] BodyKeys =
    [
        (NetError.VersionMismatch, "NETWORK_ERROR.VERSION_MISMATCH.body"),
        (NetError.ModMismatch, "NETWORK_ERROR.MOD_MISMATCH.body"),
        (NetError.Timeout, "NETWORK_ERROR.TIMEOUT.body"),
        (NetError.LobbyFull, "NETWORK_ERROR.LOBBY_FULL.body"),
        (NetError.RunInProgress, "NETWORK_ERROR.RUN_IN_PROGRESS.body"),
        (NetError.Kicked, "NETWORK_ERROR.KICKED.body"),
        (NetError.NotInSaveGame, "NETWORK_ERROR.NOT_IN_SAVE_GAME.body"),
        (NetError.InvalidJoin, "NETWORK_ERROR.INVALID_JOIN.body"),
        (NetError.Quit, "NETWORK_ERROR.QUIT.body"),
        (NetError.HostAbandoned, "NETWORK_ERROR.HOST_ABANDONED.body"),
        (NetError.StateDivergence, "NETWORK_ERROR.STATE_DIVERGENCE.body"),
        (NetError.UnknownNetworkError, "NETWORK_ERROR.UNKNOWN_ERROR.body"),
        (NetError.InternalError, "NETWORK_ERROR.INTERNAL_ERROR.body"),
        (NetError.InternalError, "INTERNAL_ERROR.description"),
    ];

    /// <summary>
    /// The reason behind a popup nobody recorded, from its text: the popup's body starts with
    /// the table text up to that text's first placeholder. Null when nothing fits.
    /// </summary>
    private static NetError? ReasonFromText(string body)
    {
        if (body.Length == 0) return null;

        NetError? best = null;
        var bestLength = 0;
        foreach (var (reason, key) in BodyKeys)
        {
            try
            {
                if (!LocString.Exists("main_menu_ui", key)) continue;
                var raw = new LocString("main_menu_ui", key).GetRawText();
                var cut = raw.IndexOf('{');
                var lead = (cut < 0 ? raw : raw[..cut]).Trim();
                if (lead.Length < 12 || lead.Length <= bestLength) continue;
                if (!body.TrimStart().StartsWith(lead, StringComparison.Ordinal)) continue;
                best = reason;
                bestLength = lead.Length;
            }
            catch
            {
                // A table without this key: the others still get their turn.
            }
        }
        return best;
    }

    /// <summary>The menu-to-joining-and-back of one more failed attempt, which is not worth a line every time.</summary>
    private static bool Retrying(CoopPlace from, CoopPlace to) =>
        (from is CoopPlace.Menu or CoopPlace.Joining) && (to is CoopPlace.Menu or CoopPlace.Joining);

    private static string Words(CoopPlace place) => place switch
    {
        CoopPlace.Between => "between screens",
        CoopPlace.Menu => "main menu",
        CoopPlace.Joining => "joining",
        CoopPlace.Lobby => "lobby",
        CoopPlace.LoadedLobby => "saved run's lobby",
        CoopPlace.OtherLobby => "another lobby screen",
        CoopPlace.RunStarting => "run starting",
        CoopPlace.Run => "run",
        _ => place.ToString(),
    };

    // ---------- the bot's own profile ----------

    /// <summary>
    /// Makes the bot's own profile fit for an unattended join. Each step is the call the game's
    /// own AutoSlay runner or "unlock" console command makes. Idempotent, and done once per
    /// progress object.
    ///
    /// Bot copies only, and never with Steam up: it writes this copy's
    /// default/&lt;clientId&gt;/.../progress.save. In the host's copy it would rewrite Mark's
    /// progress and timeline.
    ///
    /// Why each step: with tutorials on, the first Embark click opens a yes/no popup instead of
    /// sending ready. A profile that never played sends ascension cap 0 with its join, which
    /// drags the whole lobby to Ascension 0 and saves 0 as the host's preferred level; 10 never
    /// raises anything, since the lobby takes the lowest cap. The cap above 0 would open the
    /// "multiplayer ascension" notice once. A pending unlock animation would end by selecting
    /// that character.
    /// </summary>
    internal static void PrepareProfile(bool fromMenuPatch)
    {
        if (!IsBotCopy()) return;
        var save = SaveManager.Instance;
        if (!save.IsProfileInitialized) return;
        var progress = save.Progress;
        if (progress is null || ReferenceEquals(progress, _prepared)) return;
        _prepared = progress;

        try
        {
            var did = new List<string>();
            var dirty = false;

            if (progress.EnableFtues)
            {
                save.SetFtuesEnabled(false);
                did.Add("tutorials off");
            }

            if (progress.MaxMultiplayerAscension < TopAscension)
            {
                did.Add($"multiplayer ascension cap {progress.MaxMultiplayerAscension} -> {TopAscension}");
                progress.MaxMultiplayerAscension = TopAscension;
                dirty = true;
            }

            if (!save.SeenPopup(AscensionNotice))
            {
                save.MarkFtueAsComplete(AscensionNotice);
                did.Add("ascension notice marked seen");
            }

            if (UnlockCharacters)
            {
                string[] epochs =
                [
                    EpochModel.GetId<Silent1Epoch>(), EpochModel.GetId<Regent1Epoch>(),
                    EpochModel.GetId<Necrobinder1Epoch>(), EpochModel.GetId<Defect1Epoch>(),
                ];
                foreach (var id in epochs)
                {
                    if (save.IsEpochRevealed(id)) continue;
                    save.ObtainEpochOverride(id, EpochState.Revealed);
                    did.Add($"revealed {id}");
                    dirty = true;
                }
            }

            if (progress.PendingCharacterUnlock != ModelId.none)
            {
                progress.PendingCharacterUnlock = ModelId.none;
                did.Add("dropped a pending unlock animation");
                dirty = true;
            }

            if (dirty) save.SaveProgressFile();

            if (did.Count == 0)
            {
                ModEntry.Log("coop: profile: already fit for joining");
                return;
            }

            ModEntry.Log($"coop: profile: {string.Join(", ", did)}");
            if (!fromMenuPatch && NGame.Instance?.MainMenu is not null)
                ModEntry.LogError("coop: profile: prepared after the main menu opened; a join that had already started sent the old ascension cap and unlocks");
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: profile: could not prepare it: {ex.Message}");
        }
    }

    // ---------- patches (bot copies only; applied by hand, never by ModEntry's attribute scan) ----------

    private static void Patch()
    {
        if (_patched) return;
        _patched = true;
        var harmony = new Harmony(HarmonyId);
        var applied = 0;

        try
        {
            // Three Create overloads; every network failure the game reports goes through this one.
            var create = AccessTools.DeclaredMethod(typeof(NErrorPopup), nameof(NErrorPopup.Create), [typeof(NetErrorInfo)])
                ?? throw new MissingMethodException(nameof(NErrorPopup), "Create(NetErrorInfo)");
            harmony.Patch(create, postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(JoinWatcher), nameof(ErrorPopupCreated))));
            applied++;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: harmony: NErrorPopup.Create skipped: {ex.Message}; failed joins will be read from the popup's text");
        }

        try
        {
            var ready = AccessTools.DeclaredMethod(typeof(NMainMenu), "_Ready", Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(NMainMenu), "_Ready");
            harmony.Patch(ready, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(JoinWatcher), nameof(BeforeMainMenuReady))));
            applied++;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: harmony: NMainMenu._Ready skipped: {ex.Message}; the profile is prepared from the frame tick, which may come after the first join has started");
        }

        try
        {
            // Private: the --fastmp dispatch every new main menu runs from its _Ready.
            var dispatch = AccessTools.DeclaredMethod(typeof(NMainMenu), "CheckCommandLineArgs", Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(NMainMenu), "CheckCommandLineArgs");
            harmony.Patch(dispatch, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(JoinWatcher), nameof(BeforeMenuCommandLine))));
            applied++;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: harmony: NMainMenu.CheckCommandLineArgs skipped: {ex.Message}; with --fastmp join a menu built right after a run tries to join at once, while the host may still count as in that run");
        }

        try
        {
            var timeline = AccessTools.DeclaredMethod(typeof(NGame), nameof(NGame.GoToTimelineAfterRun), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(NGame), nameof(NGame.GoToTimelineAfterRun));
            harmony.Patch(timeline, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(JoinWatcher), nameof(NoTimelineAfterRun))));
            applied++;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: harmony: NGame.GoToTimelineAfterRun skipped: {ex.Message}; if the game over screen's last button is clicked, the Timeline may open over the menu (it is closed after a few seconds)");
        }

        // One line to look for in a first test: each one that is missing has its own line above.
        ModEntry.Log($"coop: harmony: {applied} of 4 bot copy patches applied");
    }

    /// <summary>
    /// Read-only postfix on NErrorPopup.Create(NetErrorInfo): why the game is about to show a
    /// network error. It runs on the main thread before the popup is added, so it also sees one
    /// that is dropped because another popup is open. A null result is our own Quit, which shows
    /// nothing.
    /// </summary>
    private static void ErrorPopupCreated(NetErrorInfo __0, NErrorPopup? __result)
    {
        try
        {
            if (__result is null || !IsBotCopy()) return;

            NetError? reason = null;
            var detail = "";
            // Both throw for an info struct that holds no error at all.
            try { reason = __0.GetReason(); } catch { }
            try { detail = __0.GetErrorString() ?? ""; } catch { }

            // One popup text for two different failures. The join attaches its mod lists only to
            // the second: same version string, different model lists.
            if (reason == NetError.VersionMismatch && HasExtraInfo(__0) is { } modelHash)
                detail = modelHash
                    ? "same game version, but the lists of cards, relics and monsters differ (model hash): a mod on one side adds or removes some"
                    : "the game versions differ";

            // Under a lock: the game may finish a join off the main thread.
            lock (Recorded)
            {
                if (Recorded.Count < 8)
                    Recorded.Enqueue(new Failure(reason, detail, Interrupted()));
            }
        }
        catch (Exception ex)
        {
            if (_patchFailureLogged) return;
            _patchFailureLogged = true;
            ModEntry.LogError($"coop: join: reading a network error failed: {ex.Message}");
        }
    }

    private static System.Reflection.FieldInfo? _extraInfoField;
    private static bool _extraInfoLooked;

    /// <summary>Whether a failure carries the join's extra info (a private field); null when that can't be read.</summary>
    private static bool? HasExtraInfo(NetErrorInfo info)
    {
        try
        {
            if (!_extraInfoLooked)
            {
                _extraInfoLooked = true;
                _extraInfoField = AccessTools.Field(typeof(NetErrorInfo), "_connectionExtraInfo");
            }
            return _extraInfoField is null ? null : _extraInfoField.GetValue(info) is not null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Prefix on NMainMenu._Ready, which never skips it: with <c>--fastmp join</c> the menu
    /// starts its join from inside _Ready, and the join request carries the profile's ascension
    /// cap and unlocks, so the profile must be right before that.
    /// </summary>
    private static void BeforeMainMenuReady()
    {
        try { PrepareProfile(fromMenuPatch: true); }
        catch (Exception ex) { ModEntry.LogError($"coop: profile: {ex.Message}"); }
    }

    /// <summary>
    /// Prefix on NMainMenu.CheckCommandLineArgs. With <c>--fastmp join</c> every new main menu
    /// opens the Join screen from here, which dials the host at once. That is right for the
    /// first menu, and wrong for one built right after a run: the host still counts as in that
    /// run while it sits on its game over screen, and answers by re-admitting this copy and
    /// losing it again. While the watcher has a pause running (after a run, after a failure)
    /// or has stopped, the game's own dispatch is skipped; the watcher opens the Join screen
    /// itself when the pause is over, by the same two calls. In any other case, and in any
    /// copy that is not a bot, the game's code runs as it is.
    /// </summary>
    private static bool BeforeMenuCommandLine(NMainMenu __instance)
    {
        try
        {
            if (!IsBotCopy() || CommandLineHelper.GetValue("fastmp") != "join") return true;

            if (_halted)
            {
                // Leaving a run on the way out builds one more main menu, which must not dial the host again.
                _heldMenu = __instance.GetInstanceId();
                ModEntry.Log("coop: join: a new main menu; its own join attempt is skipped (this copy is quitting)");
                return false;
            }

            var wait = _nextAttemptAt - DateTime.UtcNow;
            if (!_stopped && wait <= TimeSpan.Zero) return true;

            _heldMenu = __instance.GetInstanceId();

            ModEntry.Log(_stopped
                ? $"coop: join: a new main menu; its own join attempt is skipped (stopped: {_stopWhy})"
                : $"coop: join: a new main menu; its own join attempt is held back, the next try is in {wait.TotalSeconds:0} s");
            return false;
        }
        catch (Exception ex)
        {
            if (!_holdFailureLogged)
            {
                _holdFailureLogged = true;
                ModEntry.LogError($"coop: join: deciding about the menu's own join failed: {ex.Message}");
            }
            return true;
        }
    }

    /// <summary>
    /// Prefix on NGame.GoToTimelineAfterRun, the detour the game over screen's last button takes
    /// when the profile discovered something in the run (a young profile nearly always does):
    /// a new main menu with the Timeline opened over it, which nobody is there to close. In a
    /// bot copy the button's other branch runs in its place, the plain return to the main menu.
    /// The profile keeps what it discovered; it is only never shown on the Timeline.
    /// </summary>
    private static bool NoTimelineAfterRun(NGame __instance, ref Task __result)
    {
        try
        {
            if (!IsBotCopy()) return true;
            ModEntry.Log("coop: run: the game over screen asked for the Timeline; a bot copy goes straight to the main menu instead");
            __result = __instance.ReturnToMainMenuAfterRun();
            return false;
        }
        catch (Exception ex)
        {
            if (!_timelineFailureLogged)
            {
                _timelineFailureLogged = true;
                ModEntry.LogError($"coop: run: skipping the Timeline failed: {ex.Message}");
            }
            return true;
        }
    }
}
