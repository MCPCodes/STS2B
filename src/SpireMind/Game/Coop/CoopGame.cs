// An alias rather than the namespace, so a game type of the same name in the global usings can
// never make it ambiguous.
using InstanceRole = SpireMind.Core.Brain.InstanceRole;

namespace SpireMind.Game.Coop;

/// <summary>
/// The one door from ModEntry into the game side of co-op (this folder): <see cref="Start"/>
/// once at mod load with the role the mod detected, and <see cref="Tick"/> every frame. The
/// relay's own door is Game/CoopBotsAdapter.cs (<see cref="CoopBots.StartCopyAsync"/>,
/// <see cref="CoopBots.Known"/>, <see cref="CoopBots.ShowLine"/>), and the screen readers ask
/// <see cref="Teammates"/> themselves.
///
/// What a copy gets, by role:
/// <list type="bullet">
/// <item>The human's copy: each part only remembers the role. Nothing touches the game until
/// he asks for a bot copy (/bot add) or sits in a local co-op lobby or run.</item>
/// <item>A bot copy (--sts2bot=1, started without Steam): its window is put away and muted
/// (<see cref="BotWindow"/>), SIGTERM and a vanished host mean "leave, then quit"
/// (<see cref="CoopBots"/>), it joins the host's lobby, picks its character, readies and gets
/// back to the menu after a run (<see cref="JoinWatcher"/>, <see cref="LobbyDriver"/>), and it
/// follows the human's votes (<see cref="Teammates"/>).</item>
/// </list>
/// Both copies show "Mark" and the bots' characters on the nameplates of a local co-op game
/// and draw the other players' chat lines as bubbles (<see cref="RemoteBubbles"/>).
///
/// A bot copy also says in its own chat when it is stuck in a way only the human can fix (a
/// join that retrying can't fix, a ghost of an earlier copy in the lobby, a popup in its
/// window). The co-op relay already carries a bot copy's system lines into the human's chat
/// log, labelled with the bot's name, so he reads it in his own window and not only in a log.
///
/// The human's copy gets one chat command, /botstop, which asks every copy this game started
/// to leave and quit (<see cref="CoopBots.StopAllAsync"/>). It stands in until the relay has a
/// stop of its own (ICoopBots has no member for one yet), and does nothing until typed.
///
/// Each part starts on its own: one that throws is logged and left off, and the others, the
/// bridge, the chat and the brain still run. Main thread.
/// </summary>
internal static class CoopGame
{
    /// <summary>How often a bot copy looks at whether it has something to tell the human.</summary>
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(1);

    /// <summary>A trouble must last this long before it is told: a popup the driver answers itself is gone sooner.</summary>
    private static readonly TimeSpan ReportSettle = TimeSpan.FromSeconds(5);

    /// <summary>The same line is not told again sooner than this, however often the trouble comes and goes (a join retried every half minute).</summary>
    private static readonly TimeSpan ReportAgainAfter = TimeSpan.FromMinutes(10);

    /// <summary>The human's stand-in command for ending bot copies, until the relay's /bot has a stop.</summary>
    private const string StopCommand = "botstop";

    private static bool _started;

    /// <summary>True only in a copy whose role says bot; the frame tick does nothing without it.</summary>
    private static bool _botRole;
    private static bool _tickFailed;

    // What a bot copy tells the human (main thread).
    private static ChatHub? _chat;
    private static bool _reportFailed;
    private static DateTime _nextReport;
    private static string? _trouble;
    private static DateTime _troubleSince;
    private static bool _troubleTold;
    private static readonly Dictionary<string, DateTime> Told = new(StringComparer.Ordinal);

    /// <summary>
    /// Tells every part which copy this is. Call once from the mod's initializer, on the main
    /// thread, right after the role is detected and before the bridge starts, so a bot copy's
    /// window is put away and its quit handling is in place before anything can ask for it.
    /// Never throws.
    /// </summary>
    /// <param name="role">This copy's role, as ModEntry detected it (the one place it is worked out).</param>
    /// <param name="humanName">The name the chat box posts the human's lines under, for the nameplates.</param>
    /// <param name="chat">This copy's chat. A bot copy says there when it is stuck; the human's copy gets /botstop.</param>
    public static void Start(InstanceRole role, string? humanName, ChatHub? chat = null)
    {
        if (_started || role is null) return;
        _started = true;

        // First, since in a bot copy it reads the launch arguments the lobby driver needs.
        Part("the bot copy's window and quitting", () => CoopBots.Start(role));
        Part("the co-op manners", () => Teammates.Start(role));
        Part("the co-op names and bubbles", () => RemoteBubbles.Start(role, humanName));
        // The launch arguments are only read in a bot copy; the human's copy never asks for them.
        Part("the lobby driver", () => LobbyDriver.Start(role, role.IsBot ? BotArgs.Character : null));
        _botRole = role.IsBot;
        _chat = role.IsBot ? chat : null;
        if (!role.IsBot && chat is not null)
            Part("the /botstop command", () => AddStopCommand(chat));
    }

    /// <summary>
    /// /botstop in the human's window: every copy this game started gets the request to leave
    /// the lobby or run the clean way and quit, and the chat says how it went. The handler
    /// returns at once (it runs on the main thread); the waiting happens on the pool.
    /// </summary>
    private static void AddStopCommand(ChatHub chat) =>
        chat.AddCommand(StopCommand, "", "asks every bot copy this game started to leave and quit", (_, _) =>
        {
            if (CoopBots.Known.Count == 0)
                return "No bot copies started by this game are running.";
            _ = CoopBots.StopAllAsync(CancellationToken.None).ContinueWith(done =>
            {
                try { chat.Announce(done.Result); }
                catch (Exception ex) { ModEntry.LogError($"coop: /{StopCommand}: couldn't say how it went: {ex.Message}"); }
            }, TaskScheduler.Default);
            return "Asking the bot copies to leave and quit...";
        });

    /// <summary>
    /// Every frame on the main thread. Only a bot copy has work here: the join watcher and the
    /// lobby driver, which guard and switch themselves off, and the line for the human when
    /// they are stuck. In any other copy it returns at once. (The bubbles are ticked by
    /// SpeechBubbles, the bot window by CoopBots' own hook.)
    /// </summary>
    public static void Tick()
    {
        if (!_botRole) return;

        if (!_tickFailed)
        {
            try
            {
                LobbyDriver.Tick();
            }
            catch (Exception ex)
            {
                _tickFailed = true;
                ModEntry.LogError($"coop: the lobby driver's tick failed and is off; this copy no longer joins, picks or readies by itself: {ex}");
            }
        }

        if (_chat is null || _reportFailed) return;
        try
        {
            Report(_chat, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _reportFailed = true;
            ModEntry.LogError($"coop: telling the chat what this copy is stuck on failed and is off (its log still says it): {ex.Message}");
        }
    }

    private static void Part(string what, Action start)
    {
        try { start(); }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: starting {what} failed, so that part is off (the rest still runs): {ex}");
        }
    }

    // ---------- what a bot copy tells the human ----------

    /// <summary>
    /// Says a trouble once it has lasted a few seconds: as a system line in this copy's own
    /// chat, which the relay in the human's copy shows there as "Silent: ...". Nothing goes
    /// over the game's network, and nothing is sent to the model.
    /// </summary>
    private static void Report(ChatHub chat, DateTime now)
    {
        if (now < _nextReport) return;
        _nextReport = now + ReportEvery;

        // Only a real bot copy joins by itself; one started with Steam has said so in its log.
        var trouble = JoinWatcher.IsBotCopy() ? Trouble(JoinWatcher.Stopped, JoinWatcher.Status, LobbyDriver.Status) : null;
        if (trouble != _trouble)
        {
            _trouble = trouble;
            _troubleSince = now;
            _troubleTold = false;
        }
        if (trouble is null || _troubleTold || now - _troubleSince < ReportSettle) return;

        _troubleTold = true;
        if (Told.TryGetValue(trouble, out var at) && now - at < ReportAgainAfter) return;
        Told[trouble] = now;
        chat.Announce(trouble);
        ModEntry.Log($"coop: told the chat: {trouble}");
    }

    /// <summary>
    /// What the human should be told about where this copy stands, or null while all is well
    /// or the copy is only waiting its turn. Read from the two status lines, whose wording
    /// lives in <see cref="JoinWatcher"/> and <see cref="LobbyDriver"/>; a wording that changes
    /// there only loses the line here. Pure, any thread.
    /// </summary>
    internal static string? Trouble(bool joinStopped, string join, string lobby)
    {
        const string stopped = "stopped: ";
        if (joinStopped && join.StartsWith(stopped, StringComparison.Ordinal))
            return $"Can't join your lobby, and trying again won't fix it: {join[stopped.Length..]}.";

        // Between two tries the watcher's line carries the reason of the last failure.
        if (join.Contains("the ghost of an earlier copy", StringComparison.Ordinal))
            return "Can't join your lobby: it still holds a player with this copy's id, left behind by an earlier copy. Back out of the lobby and host again.";
        if (join.Contains("the lobby is full", StringComparison.Ordinal))
            return "Can't join your lobby: it is full.";
        if (join.Contains("does not hold this client id", StringComparison.Ordinal))
            return "Can't join: the saved run you loaded has no player with this copy's id.";
        if (join.StartsWith("the join watcher stopped", StringComparison.Ordinal))
            return "This copy stopped trying to join after repeated errors. Its log says why.";

        const string popup = "waiting: a popup is open in this copy's window";
        foreach (var status in new[] { lobby, join })
            if (status.StartsWith(popup, StringComparison.Ordinal))
                return $"A popup is open in this copy's window and waits for a click there{status[popup.Length..]}.";

        if (lobby.StartsWith("on a lobby screen the bot does not play", StringComparison.Ordinal))
            return "This is a custom or daily lobby, where this copy can't pick or ready up by itself. Do it in its window, or host a standard lobby.";
        if (lobby.StartsWith("the lobby driver stopped", StringComparison.Ordinal))
            return "This copy stopped picking and readying by itself after repeated errors. Its log says why.";

        // "joined; Defect is not available on this copy's profile; picked Ironclad; ready; ..."
        foreach (var part in lobby.Split("; "))
            if (part.EndsWith("is not available on this copy's profile", StringComparison.Ordinal)
                || part.StartsWith("no character is called", StringComparison.Ordinal))
                return $"{char.ToUpperInvariant(part[0])}{part[1..]}, so this copy picks another character.";

        return null;
    }
}
