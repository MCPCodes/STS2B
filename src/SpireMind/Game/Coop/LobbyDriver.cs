using System.Reflection;
// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using InstanceRole = SpireMind.Core.Brain.InstanceRole;

namespace SpireMind.Game.Coop;

/// <summary>
/// Takes a bot copy from the host's lobby into the run without anyone touching its window:
/// picks its character, readies up, readies again when something takes that back, reports the
/// moment the run is its own to play, and gets the copy back to the menu when the run is over.
/// <see cref="JoinWatcher"/> gets it into the lobby in the first place.
///
/// The integration calls <see cref="Start"/> once at mod load, with the role the mod detected
/// and the character the copy was asked to play, and <see cref="Tick"/> every frame on the
/// main thread; both also run the join watcher. Bot copies only: unless the role says this
/// copy is a bot, Start keeps everything off and Tick returns at once, so a solo game and the
/// human's own copy never reach a line that touches the game.
///
/// The brain is not started from here. In a bot copy the brain controller starts one by
/// itself as soon as it sees a run screen (BrainWiring, ForceAutostart), and plays through the
/// screen readers. The driver stays out of their way: on the menu the readers offer nothing to
/// click, and in a run the driver only watches, until the game over screen has been left alone
/// for a while or the copy is cut off.
///
/// A bot copy is always a CLIENT, and everything here is what a client's own clicks do: its
/// own character, its own ready, its own leaving. Nothing here is a call only the host may
/// make (ascension, seed, starting or abandoning the run); a lobby this copy hosts is left
/// alone. The run starts on the host the instant every player is ready, so the bot's ready is
/// the point of no return: the character is settled first.
///
/// <see cref="Status"/>, <see cref="InRun"/>, <see cref="Character"/>, <see cref="Wanted"/>
/// and <see cref="AutoReady"/> are safe from any thread. The events are raised on the main
/// thread.
/// </summary>
internal static class LobbyDriver
{
    // ---------- timing ----------

    private static readonly TimeSpan StepEvery = TimeSpan.FromMilliseconds(100);

    /// <summary>Opening the lobby screen selects the first character by itself; let that and the host's list land first.</summary>
    private static readonly TimeSpan LobbySettle = TimeSpan.FromMilliseconds(750);

    /// <summary>Between two of our own lobby actions, so each is read back before the next.</summary>
    private static readonly TimeSpan ActGap = TimeSpan.FromMilliseconds(400);

    /// <summary>A player who was here first must sit on the character a bot chose for itself this long before the bot makes way: clicking through the row passes over it.</summary>
    private static readonly TimeSpan MakeWayAfter = TimeSpan.FromSeconds(4);

    /// <summary>Embark and Unready both off with nothing to explain it: wait this long, then select another character to wake Embark.</summary>
    private static readonly TimeSpan WedgedAfter = TimeSpan.FromSeconds(3);

    /// <summary>Still wedged after this: send ready the lobby's own way and leave the screen as it is.</summary>
    private static readonly TimeSpan DirectAfter = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan FadeInPatience = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan PopupSettle = TimeSpan.FromMilliseconds(600);

    /// <summary>The ascension notice slides in after a second.</summary>
    private static readonly TimeSpan FtueSettle = TimeSpan.FromSeconds(1.6);

    /// <summary>
    /// A run that exists while the main menu is still the scene is the start. Assets load in
    /// between, slowly in a hidden window, and recovering a start that is only slow would throw
    /// the bot out of a run its teammates are entering: so wait long.
    /// </summary>
    private static readonly TimeSpan StartStuckAfter = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan DeadLobbyAfter = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FadeStuckAfter = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CutOffAfter = TimeSpan.FromSeconds(45);

    /// <summary>After asking the game for a new main menu, how long before anything counts as stuck again.</summary>
    private static readonly TimeSpan RecoverHold = TimeSpan.FromSeconds(20);

    /// <summary>The game over screen's last button stays this long for the brain's last look, then the bot leaves.</summary>
    private static readonly TimeSpan ReturnGrace = TimeSpan.FromSeconds(8);

    /// <summary>The brain clicks through the game over screen after its post-mortem. With no brain playing, the driver does, after this long.</summary>
    private static readonly TimeSpan ContinueGrace = TimeSpan.FromSeconds(150);

    /// <summary>
    /// A game over screen still up after this long cannot be left through its buttons (its
    /// summary never finished, or a fade never ended): the bot returns to the menu without them.
    /// </summary>
    private static readonly TimeSpan GameOverMax = TimeSpan.FromMinutes(6);

    /// <summary>Past <see cref="GameOverMax"/>, a return that was asked for this long ago and still has not closed the screen counts as failed, also while a fade is up.</summary>
    private static readonly TimeSpan ReturnStuckAfter = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan FaultPause = TimeSpan.FromSeconds(2);

    private const int MaxFaults = 5;

    private const int MaxFaultsLogged = 6;

    /// <summary>A second identical call can succeed where the first threw half way; after that only quitting is left.</summary>
    private const int MaxResets = 2;

    private const string TutorialsFtue = "accept_tutorials_ftue";

    /// <summary>The launch argument that names the character, without its dashes, as the game's command-line reader keys it.</summary>
    private const string CharacterArg = "sts2bot-character";

    /// <summary>The host's NetId in a lobby without Steam.</summary>
    private const ulong HostNetId = 1;

    // ---------- state (main thread) ----------

    private static bool _started;

    /// <summary>True only in a copy whose role says it is a bot; nothing runs without it.</summary>
    private static bool _active;
    private static bool _disabled;
    private static int _faults;
    private static int _faultsLogged;

    /// <summary>The character this copy was asked to play at launch, as typed; null for none.</summary>
    private static string? _asked;
    private static ulong _lastFrame = ulong.MaxValue;
    private static DateTime _nextStep;

    /// <summary>The lobby the per-lobby memory below belongs to.</summary>
    private static StartRunLobby? _lobby;

    /// <summary>The saved run's lobby last seen, so its first step is told from the later ones.</summary>
    private static LoadRunLobby? _loadedLobby;
    private static DateTime _lobbySince;
    private static DateTime _actAfter;
    private static string? _playersLine;

    /// <summary>The character the bot chose for itself in this lobby, kept so it doesn't hop about as others pick.</summary>
    private static string? _fallback;
    private static DateTime? _sameSince;

    private static DateTime? _wedgedSince;
    private static bool _woken;
    private static bool _loggedNoButtons;
    private static bool _loggedNoLoadedLobby;

    private static ulong _modalId;
    private static DateTime _modalSince;

    private static string? _stuckWhat;
    private static DateTime _stuckSince;
    private static int _resets;
    private static DateTime _holdUntil;

    private static ulong _overId;
    private static DateTime _overSince;
    private static DateTime? _returnSince;
    private static bool _returnAsked;

    /// <summary>The return to the menu was asked for once because the copy was cut off, and the run scene has been up ever since.</summary>
    private static bool _cutOffAsked;
    private static DateTime _returnAskedAt;
    private static DateTime _overActAfter;

    private static volatile bool _inRun;
    private static volatile string _status = "not a bot copy";
    private static volatile string? _character;
    private static volatile string? _wanted;
    private static volatile bool _autoReady = true;

    // ---------- for the other parts ----------

    /// <summary>
    /// The run has started and this copy controls its own player: raised once per run, on the
    /// main thread, from the frame tick (never from inside the game's own start sequence, where
    /// a handler that throws would abort the start). A handler that throws is logged and skipped.
    /// </summary>
    public static event Action? RunStarted;

    /// <summary>The run this copy was playing is gone: it ended and the copy left, or the copy was dropped. Main thread.</summary>
    public static event Action? RunEnded;

    /// <summary>
    /// True from the moment <see cref="RunStarted"/> is raised until the run is cleaned up: a
    /// co-op run exists, this copy is a client in it, and the game knows which player is ours.
    /// The first room may still be loading; the screen readers wait for that themselves.
    /// </summary>
    public static bool InRun => _inRun;

    /// <summary>One line for a human, such as "joined; picked Silent; ready; waiting for the host to start".</summary>
    public static string Status => _started ? _status : "not started";

    /// <summary>The character this copy holds in the lobby or plays in the run, as the game's id ("SILENT"); null before the first lobby.</summary>
    public static string? Character => _character;

    /// <summary>
    /// A character to play instead of the one asked for at launch, by id or name in any case
    /// ("silent", "The Silent", "random"); null goes back to that one. Changing it in the lobby
    /// makes the bot unready, pick, and ready again. It is too late once the run is starting.
    /// </summary>
    public static string? Wanted
    {
        get => _wanted;
        set => _wanted = value;
    }

    /// <summary>
    /// False keeps the bot in the lobby without readying (and takes its ready back), for when
    /// the host wants to settle something first. A ready bot never holds the host up: the run
    /// starts only when the host presses Embark too.
    /// </summary>
    public static bool AutoReady
    {
        get => _autoReady;
        set => _autoReady = value;
    }

    /// <summary>
    /// Call once at mod load, on the main thread. Also starts the join watcher. Nothing happens
    /// unless <paramref name="role"/> says this copy is a bot.
    /// </summary>
    /// <param name="role">This copy's role, as the mod detected it at load (InstanceRole.Detect).</param>
    /// <param name="character">The character the copy was asked to play (the launch argument
    /// <c>--sts2bot-character=&lt;id&gt;</c>, as BotArgs.Character has it), by id or name in any
    /// case. Null or empty: the argument is read here once, and without it the bot chooses for
    /// itself.</param>
    public static void Start(InstanceRole role, string? character = null)
    {
        if (_started) return;
        _started = true;
        try
        {
            if (role is not { IsBot: true }) return;

            _asked = string.IsNullOrWhiteSpace(character) ? AskedOnCommandLine() : character.Trim();
            _active = true;
            _status = "starting up";
            ModEntry.Log(_asked is null
                ? "coop: lobby: no character was asked for; this copy picks one nobody else holds"
                : $"coop: lobby: asked to play '{_asked}'");
            JoinWatcher.Start(role);
        }
        catch (Exception ex)
        {
            _active = false;
            ModEntry.LogError($"coop: lobby: start failed, so this copy does not join or ready by itself: {ex.Message}");
        }
    }

    /// <summary>
    /// The launch argument <c>--sts2bot-character=&lt;id&gt;</c> (or <c>--sts2bot-character &lt;id&gt;</c>),
    /// through the game's own command-line reader. Only used when <see cref="Start"/> was given
    /// no character.
    /// </summary>
    private static string? AskedOnCommandLine()
    {
        try
        {
            var value = CommandLineHelper.GetValue(CharacterArg)?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: lobby: reading --{CharacterArg} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Call every frame on the main thread. Runs the join watcher first.</summary>
    public static void Tick()
    {
        if (!_active) return;
        JoinWatcher.Tick();
        if (_disabled) return;

        var frame = Engine.GetProcessFrames();
        if (frame == _lastFrame) return;
        _lastFrame = frame;

        var now = DateTime.UtcNow;
        if (now < _nextStep) return;
        _nextStep = now + StepEvery;

        try
        {
            if (!JoinWatcher.IsBotCopy())
            {
                // The role says bot but Steam is running in this copy: the watcher says so, once.
                _status = JoinWatcher.Status;
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

    /// <summary>
    /// A step that throws is logged (the first few times ever) and tried again after a pause.
    /// <see cref="MaxFaults"/> in a row and the driver switches itself off: it must never take
    /// the game down with it.
    /// </summary>
    private static void Fault(DateTime now, Exception ex)
    {
        _faults++;
        _nextStep = now + FaultPause;
        if (_faultsLogged < MaxFaultsLogged)
        {
            _faultsLogged++;
            ModEntry.LogError($"coop: lobby: step failed ({_faults} in a row): {ex}");
        }
        if (_faults < MaxFaults) return;

        _disabled = true;
        _status = "the lobby driver stopped after repeated errors (see the log); pick and ready up in this copy's window";
        ModEntry.LogError("coop: lobby: switched off after repeated errors; this copy no longer picks or readies by itself");
    }

    // ---------- the step ----------

    private static void Step(DateTime now)
    {
        var view = JoinWatcher.Look();
        TrackRun();

        if (view.Place != CoopPlace.Lobby) _lobby = null;
        if (view.Place != CoopPlace.LoadedLobby) _loadedLobby = null;
        if (view.Place != CoopPlace.Run) _cutOffAsked = false;

        switch (view.Place)
        {
            case CoopPlace.Run:
                RunStep(now);
                return;

            case CoopPlace.RunStarting:
                Say("the run is starting");
                if (now >= _holdUntil && Stuck(now, "start", StartStuckAfter))
                    Recover(now, "a run was set up but never started");
                return;

            case CoopPlace.Between:
                BetweenStep(now);
                return;
        }

        // On the main menu from here, with no run.
        if (ModalStep(now)) return;

        switch (view.Place)
        {
            case CoopPlace.Lobby when view.Top is NCharacterSelectScreen screen && view.Lobby is { } lobby:
                NotStuck();
                _resets = 0;
                LobbyStep(now, screen, lobby);
                return;

            case CoopPlace.LoadedLobby when view.Top is NMultiplayerLoadGameScreen loaded:
                LoadedStep(now, loaded);
                return;

            case CoopPlace.OtherLobby:
                OtherLobbyStep(now, view);
                return;

            default:
                MenuStep(now, view);
                return;
        }
    }

    /// <summary>
    /// No main menu and no run: the game is starting, or one scene is replacing another, which
    /// takes a second or a few. The one way to stay here is a run scene whose run has been
    /// cleaned up and whose main menu never came.
    /// </summary>
    private static void BetweenStep(DateTime now)
    {
        _status = JoinWatcher.Status;
        if (NGame.Instance is not { RootSceneContainer: not null, CurrentRunNode: not null })
        {
            NotStuck();
            return;
        }
        if (now >= _holdUntil && Stuck(now, "run scene without a run", StartStuckAfter))
            Recover(now, "the run was cleaned up but its scene stayed, with no main menu after it");
    }

    /// <summary>
    /// The earliest reliable "the run is mine to play": a run exists, this copy is a client in
    /// it, and the game has set the local NetId (it does so in the same call that raises its own
    /// RunStarted, just before the run scene replaces the menu). Polled rather than subscribed:
    /// the game raises its event in the middle of its start sequence.
    /// </summary>
    private static void TrackRun()
    {
        var rm = RunManager.Instance;
        if (_inRun)
        {
            // Only the run going away ends it: a read that fails for a frame must not.
            if (rm.IsInProgress) return;
            _inRun = false;
            ModEntry.Log("coop: run: over for this copy (the run was cleaned up)");
            Raise(RunEnded, nameof(RunEnded));
            return;
        }

        // NetService is null before the first run and stale after one: only read while a run is in progress.
        if (!rm.IsInProgress || LocalContext.NetId is not { } id) return;
        if (rm.NetService is not { Type: NetGameType.Client } net || net.NetId != id) return;

        Player? me;
        try { me = rm.DebugOnlyGetState() is { } state ? LocalContext.GetMe(state) : null; }
        catch { me = null; } // throws until the run holds our player
        if (me is null) return;

        _inRun = true;
        _character = Key(me.Character);
        ModEntry.Log($"coop: run: started; this copy is player {id}, {Pretty(_character)}");
        Raise(RunStarted, nameof(RunStarted));
    }

    private static void Raise(Action? handlers, string name)
    {
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((Action)handler)(); }
            catch (Exception ex) { ModEntry.LogError($"coop: a {name} handler failed: {ex.Message}"); }
        }
    }

    // ---------- popups on the menu ----------

    /// <summary>
    /// True while a popup is open, so nothing else is tried under it. Error popups are the join
    /// watcher's. The two that can stand between a bot and its ready are answered the way a
    /// person would; anything else waits for a person.
    /// </summary>
    private static bool ModalStep(DateTime now)
    {
        if (NModalContainer.Instance?.OpenModal is not Node modal || !GodotObject.IsInstanceValid(modal))
        {
            _modalId = 0;
            return false;
        }
        if (modal is NErrorPopup) return true;

        if (modal.GetInstanceId() != _modalId)
        {
            _modalId = modal.GetInstanceId();
            _modalSince = now;
        }
        var age = now - _modalSince;

        switch (modal)
        {
            case NAcceptTutorialsFtue:
                // Embark opened this instead of sending ready (the profile was not prepared).
                // Its No button turns tutorials off and presses Embark again, so ready is sent
                // then. Clearing the popup instead would leave Embark disabled with nothing sent.
                if (age < PopupSettle) return true;
                var no = Nodes.Get<NVerticalPopup>(modal, "VerticalPopup")?.NoButton;
                if (no is { IsEnabled: true } && Clicks.Once(no, no.ForceClick, "the tutorials popup's No button").Ok)
                    ModEntry.Log("coop: lobby: answered the tutorials question with No (this sends ready)");
                return true;

            case NFtue:
                // The "multiplayer ascension" notice: one confirm button, which marks it seen.
                if (age < FtueSettle) return true;
                var confirm = Nodes.Get<NButton>(modal, "%FtueConfirmButton");
                if (confirm is { IsEnabled: true })
                {
                    if (Clicks.Once(confirm, confirm.ForceClick, "the notice's confirm button").Ok)
                        ModEntry.Log($"coop: lobby: dismissed {modal.GetType().Name}");
                    return true;
                }
                break;
        }

        Say($"waiting: a popup is open in this copy's window ({modal.GetType().Name})");
        return true;
    }

    // ---------- the lobby ----------

    private static void LobbyStep(DateTime now, NCharacterSelectScreen screen, StartRunLobby lobby)
    {
        var net = lobby.NetService;
        var myId = net.NetId;

        if (!ReferenceEquals(lobby, _lobby))
        {
            _lobby = lobby;
            _lobbySince = now;
            _actAfter = now;
            _playersLine = null;
            _fallback = null;
            _sameSince = null;
            _wedgedSince = null;
            _woken = false;
            ModEntry.Log($"coop: lobby: joined as player {myId}");
        }

        // Players is a list of structs that the lobby replaces entry by entry: work on a copy.
        var players = lobby.Players.ToList();
        var mine = players.FindIndex(p => p.id == myId);
        if (mine < 0)
        {
            Say("joined; waiting to appear in the lobby's player list");
            return;
        }
        var me = players[mine];
        var current = Key(me.character);
        _character = current;
        LogPlayers(players, myId, lobby.Ascension);

        if (!net.IsConnected)
        {
            Say("joined, but the connection to the host dropped; waiting for the game to close the lobby");
            return;
        }

        // Everyone ready: the host is starting the run (or has), and from the begin message on
        // the lobby ignores character and ready changes anyway.
        if (lobby.IsAboutToBeginGame())
        {
            Say($"joined; picked {Pretty(current)}; ready; the run is starting");
            return;
        }

        if (now - _lobbySince < LobbySettle || now < _actAfter || FadingIn(now)) return;

        var buttons = Buttons(screen);
        var others = players.Where(p => p.id != myId).ToList();
        var target = Target(now, myId, buttons, others, out var note, out var settling);
        var targetKey = target is null ? current : ButtonKey(target);
        var right = targetKey == current;
        var wantReady = _autoReady;
        var lead = note is null ? "joined" : $"joined; {note}";

        if (me.isReady)
        {
            if (right && wantReady)
            {
                _wedgedSince = null;
                _woken = false;
                Say($"{lead}; picked {Pretty(current)}; ready; {WaitingFor(others)}");
                return;
            }
            Unready(now, screen, lobby, right ? "asked to wait" : $"to switch to {Pretty(targetKey)}");
            return;
        }

        if (!right && target is not null)
        {
            Pick(now, target, buttons, current);
            Say($"{lead}; picking {Pretty(targetKey)}");
            return;
        }

        if (!wantReady)
        {
            Say($"{lead}; picked {Pretty(current)}; not readying (asked to wait)");
            return;
        }

        if (settling)
        {
            // Ready is the point of no return (the host may be waiting on us alone), so a clash
            // over a character the bot chose for itself is settled first. Never longer than
            // MakeWayAfter: after that the bot has moved on or stays.
            Say($"{lead}; picked {Pretty(current)}; another player holds it too, so waiting a moment before readying");
            return;
        }

        Say($"{lead}; picked {Pretty(current)}; readying");
        Ready(now, screen, lobby, buttons, current);
    }

    private static string WaitingFor(List<LobbyPlayer> others)
    {
        if (others.Count == 0) return "waiting for the host";
        var host = others.FindIndex(p => p.id == HostNetId);
        if (host >= 0 && !others[host].isReady) return "waiting for the host to start";
        return others.Any(p => !p.isReady) ? "waiting for the other players" : "the run is starting";
    }

    private static void LogPlayers(List<LobbyPlayer> players, ulong myId, int ascension)
    {
        var line = string.Join(", ", players.OrderBy(p => p.slotId).Select(p =>
            $"{p.id}{(p.id == myId ? " (me)" : p.id == HostNetId ? " (host)" : "")} {Pretty(Key(p.character))}{(p.isReady ? " ready" : "")}"));
        line = $"{line}; ascension {ascension}";
        if (line == _playersLine) return;
        _playersLine = line;
        ModEntry.Log($"coop: lobby: players: {line}");
    }

    /// <summary>The character buttons in screen order: Ironclad, Silent, Regent, Necrobinder, Defect, then Random.</summary>
    private static List<NCharacterSelectButton> Buttons(NCharacterSelectScreen screen)
    {
        var buttons = new List<NCharacterSelectButton>();
        if (Nodes.Get<Godot.Control>(screen, "CharSelectButtons/ButtonContainer") is { } container)
        {
            foreach (var child in container.GetChildren())
                if (child is NCharacterSelectButton button && Nodes.Alive(button) && button.Character is not null)
                    buttons.Add(button);
        }

        if (buttons.Count == 0 && !_loggedNoButtons)
        {
            _loggedNoButtons = true;
            ModEntry.LogError("coop: lobby: no character buttons found on the lobby screen; keeping the character the lobby gave");
        }
        return buttons;
    }

    /// <summary>A button a click can select: shown (Random is hidden unless someone owns every character) and not locked on this profile.</summary>
    private static bool Pickable(NCharacterSelectButton button) => button.Visible && !button.IsLocked;

    /// <summary>
    /// The button of the character to hold. The one asked for when this profile can pick it.
    /// Otherwise one the bot chooses for itself: the first nobody else holds, else the first
    /// that is not the host's, else the first. Null when there is nothing to pick from.
    /// </summary>
    /// <param name="settling">True while a player who was here first holds the character the
    /// bot chose for itself and the bot is still waiting to see whether that player stays on
    /// it: the bot keeps the character for now, and should not ready on it yet.</param>
    private static NCharacterSelectButton? Target(DateTime now, ulong myId, List<NCharacterSelectButton> buttons, List<LobbyPlayer> others, out string? note, out bool settling)
    {
        note = null;
        settling = false;
        var asked = (_wanted ?? _asked)?.Trim();
        if (!string.IsNullOrEmpty(asked))
        {
            var key = Key(asked);
            var button = buttons.FirstOrDefault(b => Answers(b, key))
                ?? (key.StartsWith("THE", StringComparison.Ordinal) ? buttons.FirstOrDefault(b => Answers(b, key[3..])) : null);
            if (button is not null && Pickable(button))
            {
                _fallback = null;
                _sameSince = null;
                return button;
            }
            note = button is null
                ? $"no character is called '{asked}'"
                : $"{Pretty(ButtonKey(button))} is not available on this copy's profile";
        }

        var free = buttons.Where(b => !b.IsRandom && Pickable(b)).ToList();
        if (free.Count == 0) return null;

        var taken = others.Select(p => Key(p.character)).ToHashSet(StringComparer.Ordinal);
        var hostAt = others.FindIndex(p => p.id == HostNetId);
        var hostHas = hostAt >= 0 ? Key(others[hostAt].character) : null;

        var kept = _fallback is null ? null : free.FirstOrDefault(b => ButtonKey(b) == _fallback);
        if (kept is null)
        {
            kept = free.FirstOrDefault(b => !taken.Contains(ButtonKey(b)))
                ?? free.FirstOrDefault(b => ButtonKey(b) != hostHas)
                ?? free[0];
            _fallback = ButtonKey(kept);
            _sameSince = null;
            return kept;
        }

        // Someone who was here first settled on the character the bot chose for itself: the
        // host, or a bot with a lower id that chose in the same moment. Make way, when a free
        // one is left. The lower id never moves, so two bots cannot keep swapping. The wait is
        // also for bots: every player holds the Ironclad for its first second in a lobby.
        var first = others.FindIndex(p => p.id < myId && Key(p.character) == _fallback);
        var other = first < 0 ? null : free.FirstOrDefault(b => !taken.Contains(ButtonKey(b)));
        if (other is null)
        {
            // Nobody to make way for, or nowhere to go: the character stays.
            _sameSince = null;
            return kept;
        }
        _sameSince ??= now;
        if (now - _sameSince.Value < MakeWayAfter)
        {
            settling = true;
            return kept;
        }

        var who = others[first].id == HostNetId ? "the host" : $"player {others[first].id}";
        ModEntry.Log($"coop: lobby: {who} picked {Pretty(_fallback)} too; making way for {Pretty(ButtonKey(other))}");
        _fallback = ButtonKey(other);
        _sameSince = null;
        return other;
    }

    /// <summary>
    /// Selects a character the way a click does: the button selects itself when it gains focus,
    /// by this same public method, which tells the screen, which tells the lobby, which tells
    /// the host. Never called on a locked button (that disables Embark and sends nothing).
    /// </summary>
    private static void Pick(DateTime now, NCharacterSelectButton target, List<NCharacterSelectButton> buttons, string current)
    {
        if (target.IsSelected)
        {
            // The screen shows it selected while the lobby holds another: Select would do nothing.
            // Selecting any other unlocked one first makes the next Select count.
            if (buttons.FirstOrDefault(b => !ReferenceEquals(b, target) && !b.IsRandom && Pickable(b) && !b.IsSelected) is not { } other) return;
            ModEntry.Log($"coop: lobby: the screen shows {Pretty(ButtonKey(target))} but the lobby holds {Pretty(current)}; selecting {Pretty(ButtonKey(other))} first");
            other.Select();
            _actAfter = now + ActGap;
            return;
        }

        // Disabled while an unlock animation plays over it; a click could not land either.
        if (!target.IsEnabled && !Wedged(now, WedgedAfter)) return;

        target.Select();
        _wedgedSince = null;
        _actAfter = now + ActGap;
        ModEntry.Log($"coop: lobby: picked {Pretty(ButtonKey(target))} (was {Pretty(current)})");
    }

    /// <summary>
    /// Readies up by pressing Embark, which for a client only means "I am ready". The click runs
    /// the screen's own handler: it sends ready, locks the character buttons and shows the
    /// waiting panel with Unready.
    /// </summary>
    private static void Ready(DateTime now, NCharacterSelectScreen screen, StartRunLobby lobby, List<NCharacterSelectButton> buttons, string current)
    {
        // With the tutorials question unanswered, Embark opens it instead of sending ready.
        if (!SaveManager.Instance.SeenFtue(TutorialsFtue))
        {
            ModEntry.Log("coop: lobby: tutorials were still on in this copy's profile; turning them off before Embark");
            SaveManager.Instance.SetFtuesEnabled(false);
            _actAfter = now + ActGap;
            return;
        }

        var embark = Nodes.Get<NConfirmButton>(screen, "ConfirmButton");
        if (embark is { IsEnabled: true })
        {
            if (!Clicks.Once(embark, embark.ForceClick, "the Embark button").Ok) return;
            _wedgedSince = null;
            _woken = false;
            _actAfter = now + ActGap;
            ModEntry.Log($"coop: lobby: ready as {Pretty(current)} (pressed Embark)");
            return;
        }

        var unready = Nodes.Get<NBackButton>(screen, "UnreadyButton");
        if (unready is { IsEnabled: true })
        {
            // The screen believes we are ready and the lobby does not: Unready puts the buttons back.
            if (!Clicks.Once(unready, unready.ForceClick, "the Unready button").Ok) return;
            _actAfter = now + ActGap;
            ModEntry.Log("coop: lobby: the screen showed ready while the lobby did not; pressed Unready to reset it");
            return;
        }

        // Neither button: an unlock animation is playing, or a locked character holds the selection.
        if (!Wedged(now, WedgedAfter)) return;

        if (!_woken && buttons.FirstOrDefault(b => !b.IsRandom && Pickable(b) && b.IsEnabled && !b.IsSelected) is { } other)
        {
            // Selecting an unlocked character enables Embark again; the next steps pick ours back.
            _woken = true;
            ModEntry.Log($"coop: lobby: Embark stayed disabled; selecting {Pretty(ButtonKey(other))} to wake it");
            other.Select();
            _actAfter = now + ActGap;
            return;
        }

        if (!Wedged(now, DirectAfter)) return;

        // The lobby's own call, which Embark's handler makes: the same message to the host. The
        // screen stays looking unready and its Unready button stays off.
        ModEntry.Log($"coop: lobby: Embark is still disabled; sending ready as {Pretty(current)} without the button");
        lobby.SetReady(true);
        _wedgedSince = null;
        _actAfter = now + ActGap;
    }

    private static void Unready(DateTime now, NCharacterSelectScreen screen, StartRunLobby lobby, string why)
    {
        var unready = Nodes.Get<NBackButton>(screen, "UnreadyButton");
        if (unready is { IsEnabled: true })
        {
            if (!Clicks.Once(unready, unready.ForceClick, "the Unready button").Ok) return;
            _wedgedSince = null;
            _actAfter = now + ActGap;
            ModEntry.Log($"coop: lobby: unready ({why})");
            return;
        }

        // Unready is off while the lobby holds us ready only after ready was sent without the button.
        if (!Wedged(now, WedgedAfter)) return;
        ModEntry.Log($"coop: lobby: unready without the button ({why})");
        lobby.SetReady(false);
        _wedgedSince = null;
        _actAfter = now + ActGap;
    }

    /// <summary>True once the same wedged state has lasted <paramref name="patience"/>.</summary>
    private static bool Wedged(DateTime now, TimeSpan patience)
    {
        _wedgedSince ??= now;
        return now - _wedgedSince.Value >= patience;
    }

    // ---------- the lobby of a saved run ----------

    /// <summary>
    /// A saved co-op run the host continues. The save fixes the character; all there is to do is
    /// ready. Its Confirm button is the client's ready, as Embark is in a new lobby.
    /// </summary>
    private static void LoadedStep(DateTime now, NMultiplayerLoadGameScreen screen)
    {
        // The lobby is a private field with no accessor.
        if (!TryReadField(screen, "_runLobby", out var value))
        {
            NotStuck();
            if (!_loggedNoLoadedLobby)
            {
                _loggedNoLoadedLobby = true;
                ModEntry.LogError("coop: lobby: NMultiplayerLoadGameScreen._runLobby is missing in this game build; ready up in this copy's window");
            }
            _status = "in a saved run's lobby, which this build hides from the mod; ready up in this copy's window";
            return;
        }

        if (value is not LoadRunLobby lobby)
        {
            DeadLobby(now);
            return;
        }

        NotStuck();
        _resets = 0;
        var net = lobby.NetService;
        if (net.Type != NetGameType.Client)
        {
            Say("this copy hosts a saved run's lobby; leaving it alone");
            return;
        }

        var myId = net.NetId;
        var saved = lobby.Run.Players.FirstOrDefault(p => p.NetId == myId)?.CharacterId?.Entry;
        var current = saved is null ? "" : Key(saved);
        _character = current;
        if (!ReferenceEquals(lobby, _loadedLobby))
        {
            _loadedLobby = lobby;
            _lobbySince = now;
            _actAfter = now;
            ModEntry.Log($"coop: lobby: joined a saved run's lobby as player {myId}, {Pretty(current)}");
        }
        var ready = lobby.IsPlayerReady(myId);
        var lead = $"joined a saved run as {Pretty(current)}";

        if (!net.IsConnected)
        {
            Say($"{lead}, but the connection to the host dropped; waiting for the game to close the lobby");
            return;
        }
        if (lobby.IsAboutToBeginGame())
        {
            Say($"{lead}; ready; the run is starting");
            return;
        }
        if (now - _lobbySince < LobbySettle || now < _actAfter || FadingIn(now)) return;

        var wantReady = _autoReady;
        if (ready == wantReady)
        {
            Say(ready ? $"{lead}; ready; waiting for the host to start" : $"{lead}; not readying (asked to wait)");
            return;
        }

        NClickableControl? button = wantReady
            ? Nodes.Get<NConfirmButton>(screen, "ConfirmButton")
            : Nodes.Get<NBackButton>(screen, "UnreadyButton");
        Say(wantReady ? $"{lead}; readying" : $"{lead}; taking ready back");
        if (button is not { IsEnabled: true }) return;
        if (!Clicks.Once(button, button.ForceClick, wantReady ? "the Confirm button" : "the Unready button").Ok) return;
        _actAfter = now + ActGap;
        ModEntry.Log(wantReady ? $"coop: lobby: ready in the saved run as {Pretty(current)} (pressed Confirm)" : "coop: lobby: unready in the saved run (asked to wait)");
    }

    /// <summary>
    /// A main menu built after a run fades in for two seconds, and a quick join lands in the
    /// lobby under that fade. Wait it out, but only for the lobby's first seconds: a fade that
    /// never ends must not keep the bot from readying.
    /// </summary>
    private static bool FadingIn(DateTime now) =>
        now - _lobbySince < FadeInPatience && NGame.Instance?.Transition?.InTransition == true;

    private static bool TryReadField(object target, string name, out object? value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, flags) is not { } field) continue;
            value = field.GetValue(target);
            return true;
        }
        value = null;
        return false;
    }

    // ---------- everything else on the menu ----------

    private static void OtherLobbyStep(DateTime now, CoopView view)
    {
        if (view.Top is NCharacterSelectScreen { Lobby: null })
        {
            DeadLobby(now);
            return;
        }

        NotStuck();
        Say($"on a lobby screen the bot does not play ({view.Top?.GetType().Name}); pick and ready up in this copy's window");
    }

    /// <summary>
    /// A lobby screen on top whose lobby is gone, with no run: a start that threw before the run
    /// existed. The game disconnected, showed its error and left the menu faded to black. The
    /// screen cannot be closed like a live one (its close handler reads the lobby it no longer
    /// has), so the menu is rebuilt.
    /// </summary>
    private static void DeadLobby(DateTime now)
    {
        Say("the lobby is gone but its screen is still open; waiting, then rebuilding the main menu");
        if (now >= _holdUntil && Stuck(now, "dead lobby", DeadLobbyAfter))
            Recover(now, "a lobby screen was left open without its lobby (a run start that failed)");
    }

    private static void MenuStep(DateTime now, CoopView view)
    {
        _status = JoinWatcher.Status;

        // A new menu fades in for two seconds. One still faded out long after, with no join
        // under way, is the black screen a failed start leaves.
        if (NGame.Instance?.Transition?.InTransition != true || view.AttemptRunning)
        {
            NotStuck();
            if (NGame.Instance?.Transition?.InTransition != true) _resets = 0;
            return;
        }
        if (now >= _holdUntil && Stuck(now, "faded menu", FadeStuckAfter))
            Recover(now, "the main menu stayed faded out");
    }

    private static bool Stuck(DateTime now, string what, TimeSpan patience)
    {
        if (_stuckWhat != what)
        {
            _stuckWhat = what;
            _stuckSince = now;
        }
        return now - _stuckSince >= patience;
    }

    private static void NotStuck() => _stuckWhat = null;

    /// <summary>
    /// Back to a known place: the game's own "return to main menu", which fades out, cleans up
    /// any run (that disconnects from the host with a proper goodbye) and builds a new menu.
    /// Never called with a live lobby on top: replacing the scene under a lobby skips the
    /// lobby's own leave and the host keeps a ghost.
    /// CLIENT: ends only this copy's session. On a host it would end the session for everyone,
    /// which is one more reason this class runs in bot copies only.
    /// </summary>
    private static void Recover(DateTime now, string why)
    {
        NotStuck();
        if (NGame.Instance is not { } game) return;

        if (_resets >= MaxResets)
        {
            QuitCopy($"{why}, and rebuilding the main menu {MaxResets} times did not help");
            return;
        }

        _resets++;
        _holdUntil = now + RecoverHold;
        ModEntry.LogError($"coop: recovering: {why}; rebuilding the main menu (try {_resets} of {MaxResets})");
        _status = $"recovering: {why}";
        TaskHelper.RunSafely(game.ReturnToMainMenu());
    }

    /// <summary>
    /// The last resort the game's code leaves: the menu could not be rebuilt, so this copy can
    /// neither play nor join.
    /// </summary>
    private static void QuitCopy(string why) => QuitCleanly(why);

    /// <summary>
    /// Quits this copy without leaving a ghost player on the host: first the game's own leave
    /// (the host only removes a client whose goodbye packet arrives; the game's Quit by itself
    /// sends none, and neither does closing the window), then the game's own Quit, which saves
    /// settings and progress. Main thread. Does nothing in a copy that is not a bot.
    /// </summary>
    internal static void QuitCleanly(string why)
    {
        if (!_active || !JoinWatcher.IsBotCopy()) return;

        ModEntry.LogError($"coop: QUITTING this copy: {why}");
        _status = $"quit: {why}";
        _disabled = true;
        JoinWatcher.Halt();
        try
        {
            LeaveSession();
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: leaving before the quit failed: {ex.Message}");
        }

        try
        {
            // The game's own Quit saves settings and progress first. If it is gone or throws,
            // the engine's quit still ends the process: a copy that said it quits must not linger.
            if (NGame.Instance is { } game && GodotObject.IsInstanceValid(game)) game.Quit();
            else (Engine.GetMainLoop() as SceneTree)?.Quit();
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: the game's quit failed ({ex.Message}); quitting through the engine");
            try { (Engine.GetMainLoop() as SceneTree)?.Quit(); }
            catch (Exception inner) { ModEntry.LogError($"coop: the engine's quit failed too: {inner.Message}"); }
        }
    }

    /// <summary>
    /// Leaves whatever ties this copy to the host, the way its own buttons would. In a run: the
    /// pause menu's Disconnect (the goodbye is sent and flushed inside the call). In a lobby,
    /// or with a join under way: closing the screen, which is Back (it cleans the lobby up and
    /// disconnects; a join is cancelled). CLIENT only: the same calls in a host's copy would
    /// end the session for everyone.
    /// </summary>
    private static void LeaveSession()
    {
        var rm = RunManager.Instance;
        if (rm.IsInProgress)
        {
            if (rm.NetService is { Type: NetGameType.Client, IsConnected: true } net)
            {
                net.Disconnect(NetError.Quit);
                ModEntry.Log("coop: left the run (disconnected with the goodbye)");
            }
            return;
        }

        var view = JoinWatcher.Look();
        if (view.Stack is not { } stack) return;
        var leave = view.Place switch
        {
            CoopPlace.Lobby or CoopPlace.LoadedLobby => "left the lobby",
            CoopPlace.Joining when view.Top is NJoinFriendScreen => "cancelled the join",
            _ => null,
        };
        if (leave is null) return;

        stack.Pop();
        ModEntry.Log($"coop: {leave} (closed its screen, as Back does)");
    }

    // ---------- in the run ----------

    private static void RunStep(DateTime now)
    {
        var rm = RunManager.Instance;
        if (rm.NetService is not { Type: NetGameType.Client } net)
        {
            NotStuck();
            _status = "in a run this copy did not join from a lobby";
            return;
        }

        // The overlay stack hangs off the run's global UI, which is null while the scene loads.
        if (NRun.Instance is not { GlobalUi: not null })
        {
            NotStuck();
            Say("the run is loading");
            return;
        }

        if (NOverlayStack.Instance?.Peek() is NGameOverScreen over && Nodes.Alive(over))
        {
            NotStuck();
            GameOverStep(now, over);
            return;
        }
        _overId = 0;

        // Cut off with the run scene still up and no game over screen: the game returns to the
        // menu by itself for every drop except a host who abandoned or left after the end. A
        // fade means that return is under way, unless it is the fade of our own return, which
        // then never finished.
        if (!net.IsConnected && NGame.Instance is { } game && !Nodes.ModalOpen()
            && (_cutOffAsked || game.Transition?.InTransition != true))
        {
            Say("cut off from the host; waiting for the game to end the run");
            if (now >= _holdUntil && Stuck(now, "cut off", CutOffAfter))
            {
                NotStuck();
                if (_cutOffAsked)
                {
                    // Asked once already and the run scene is still here: count it, and quit in the end.
                    Recover(now, "cut off from the host, and the run scene stayed after the return to the main menu");
                    return;
                }
                _cutOffAsked = true;
                _holdUntil = now + RecoverHold;
                ModEntry.Log("coop: run: cut off from the host and no game over screen came; returning to the main menu");
                TaskHelper.RunSafely(game.ReturnToMainMenuAfterRun());
            }
            return;
        }

        NotStuck();
        if (_inRun) _resets = 0;
        Say(_inRun ? $"in the run as {Pretty(_character)}" : "the run is loading");
    }

    /// <summary>
    /// The end of a run. The brain reads this screen for its post-mortem and clicks through its
    /// pages (GameOverReader); with no brain playing, the driver clicks them after a long wait.
    /// The last button is never clicked: for a client it either returns to the main menu or,
    /// when the profile discovered an epoch in the run (a young profile nearly always does),
    /// detours through the Timeline. The driver makes the first of those calls itself.
    /// CLIENT only: the host's button also tells the others the session is over.
    /// </summary>
    private static void GameOverStep(DateTime now, NGameOverScreen over)
    {
        if (over.GetInstanceId() != _overId)
        {
            _overId = over.GetInstanceId();
            _overSince = now;
            _returnSince = null;
            _returnAsked = false;
            _overActAfter = now;
        }
        Say("the run is over; reading the result, then returning to the main menu");

        if (NGame.Instance is not { } game) return;
        var fading = game.Transition?.InTransition == true;

        // The last net: nothing below runs while a popup stays open or a fade never ends, and
        // the screen's last button only appears once its summary has played to the end.
        var age = now - _overSince;
        if (age >= GameOverMax && now >= _holdUntil)
        {
            if (_returnAsked)
            {
                if (now - _returnAskedAt >= ReturnStuckAfter)
                {
                    // Asked long ago and the screen is still here, fading or not: only a rebuild is left.
                    Recover(now, "the game over screen did not close");
                    return;
                }
            }
            else if (!fading || age >= GameOverMax + ReturnStuckAfter)
            {
                // A fade that is up may be a return someone else just asked for (the brain's
                // click on the last button): that one gets a minute more before it counts as stuck.
                _returnAsked = true;
                _returnAskedAt = now;
                _overActAfter = now + RecoverHold;
                ModEntry.LogError($"coop: run: still on the game over screen after {age.TotalMinutes:0} min; returning to the main menu without its buttons");
                TaskHelper.RunSafely(game.ReturnToMainMenuAfterRun());
                return;
            }
        }

        if (Nodes.ModalOpen() || fading) return;
        if (now < _overActAfter || now < _holdUntil) return;

        if (_returnAsked)
        {
            // Asked to leave a while ago and the screen is still here, not even fading.
            Recover(now, "the game over screen did not close");
            return;
        }

        var home = Nodes.Get<NReturnToMainMenuButton>(over, "%MainMenuButton");
        if (Usable(home))
        {
            _returnSince ??= now;
            if (now - _returnSince.Value < ReturnGrace) return;

            // Disabled first, as its own handler does, so nothing else presses it meanwhile.
            var left = Clicks.Once(home, () =>
            {
                home.Disable();
                TaskHelper.RunSafely(game.ReturnToMainMenuAfterRun());
            }, "the return to main menu button");
            if (!left.Ok) return;
            _returnAsked = true;
            _returnAskedAt = now;
            _overActAfter = now + RecoverHold;
            ModEntry.Log("coop: run: over; returning to the main menu (the game's own call, without the Timeline detour)");
            return;
        }
        _returnSince = null;

        if (now - _overSince < ContinueGrace) return;
        foreach (var name in new[] { "%ContinueButton", "%LeaderboardButton" })
        {
            var next = Nodes.Get<NGameOverContinueButton>(over, name);
            if (!Usable(next)) continue;
            if (!Clicks.Once(next, next.ForceClick, "the game over screen's continue button").Ok) return;
            _overActAfter = now + TimeSpan.FromSeconds(5);
            ModEntry.Log($"coop: run: nobody clicked through the game over screen for {ContinueGrace.TotalSeconds:0} s; pressed {name.TrimStart('%')}");
            return;
        }
    }

    private static bool Usable([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] NClickableControl? button) =>
        Nodes.Alive(button) && Nodes.IsShown(button) && button.IsEnabled;

    // ---------- names ----------

    private static string ButtonKey(NCharacterSelectButton button) =>
        button.IsRandom ? "RANDOM" : Key(button.Character);

    /// <summary>
    /// Whether a button is the one an asked-for name means: its key ("SILENT", "RANDOM"), or for
    /// the random pick also the game's own id for it, which is what the launcher passes.
    /// </summary>
    private static bool Answers(NCharacterSelectButton button, string key) =>
        ButtonKey(button) == key || (button.IsRandom && Key(button.Character.Id.Entry) == key);

    /// <summary>A character as a key: its id's entry ("SILENT"); "RANDOM" for the random choice; "" for none.</summary>
    private static string Key(CharacterModel? character) => character switch
    {
        null => "",
        RandomCharacter => "RANDOM",
        _ => Key(character.Id.Entry),
    };

    /// <summary>Letters and digits in upper case, so "the silent", "Silent" and "SILENT" compare.</summary>
    private static string Key(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static string Pretty(string? key) =>
        string.IsNullOrEmpty(key) ? "no character" : char.ToUpperInvariant(key[0]) + key[1..].ToLowerInvariant();

    /// <summary>The status line, logged once each time it changes.</summary>
    private static void Say(string status)
    {
        if (status == _status) return;
        _status = status;
        ModEntry.Log($"coop: {status}");
    }
}
