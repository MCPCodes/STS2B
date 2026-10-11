using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Platform.Null;
using SpireMind.Game;

namespace SpireMind.Patches;

/// <summary>
/// Who is who in a local co-op game, so the human reads "Mark" and "Silent" on the nameplates
/// instead of "Test Host" and "Test Client 1".
///
/// The game never sends names. Every copy turns a player's NetId into text by itself, each
/// time it draws one, and in a local (ENet) game that text comes from
/// NullPlatformUtilStrategy.GetPlayerName. <see cref="CoopNamePatch"/> answers from
/// <see cref="NameOf"/> there instead. It only changes what this copy draws: names are in
/// nothing the copies compare, so two copies showing different names cannot fall out of sync.
///
/// How a copy knows the mapping, with no HTTP and no help from the other copy: the game is
/// lockstep, so every copy holds the whole session, each player's NetId and character, in the
/// lobby's list and later in the run.
/// <list type="bullet">
/// <item>The human's copy names its own player <see cref="HumanName"/> and every other player
/// after the character it plays. The only local join the game's menus offer dials this Mac,
/// so in such a session everyone else is a bot copy.</item>
/// <item>A bot's copy names its own player after its character, the host
/// <see cref="HumanName"/>, and any other player after its character. Nobody reads that
/// window; it is kept right so a screenshot of it makes sense.</item>
/// </list>
/// Which of the two this copy is comes from ModEntry's InstanceRole through
/// <see cref="Start"/>, never from the command line here; until it is told, a copy counts as
/// the human's. Two bots on one character become "Ironclad" and "Ironclad 2", in the run's
/// player order, which is the same on every copy.
///
/// Inert outside a local co-op lobby or run: with no such session there are no names, the
/// patch hands back what the game said, no label is touched and no game event is listened
/// to. Steam co-op never comes here (its names come from Steam's own strategy). Main thread,
/// from the frame tick, except <see cref="NameOf"/>, which any thread may call.
/// </summary>
internal static class CoopNames
{
    /// <summary>How often the session is read again. A lobby row asks for its name the moment a bot joins, so it is briefly the stock one.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    /// <summary>The names the bots answer to, as in Identity and ChatHub.KnownNames. Other characters use their title without "The ".</summary>
    private static readonly string[] Known = ["Ironclad", "Silent", "Defect", "Regent", "Necrobinder"];

    private static readonly IReadOnlyDictionary<ulong, string> NoNames = new Dictionary<ulong, string>();
    private static readonly IReadOnlyDictionary<ulong, CharacterModel?> NoCharacters = new Dictionary<ulong, CharacterModel?>();

    /// <summary>
    /// NetId to name. Never changed once published, only replaced: the game asks for names on
    /// every combat state change, and its leaderboard code may ask from another thread.
    /// </summary>
    private static volatile IReadOnlyDictionary<ulong, string> _names = NoNames;

    /// <summary>NetId to character for the same session, for the bubbles' colour. Main thread.</summary>
    private static IReadOnlyDictionary<ulong, CharacterModel?> _characters = NoCharacters;

    private static string? _human;

    /// <summary>Whether this is a bot's copy, as <see cref="Start"/> was told; null until then, and such a copy counts as the human's.</summary>
    private static bool? _botCopy;
    private static bool _hooked;
    private static bool _off;
    private static bool _relabelOff;
    private static DateTime _next;

    /// <summary>Session reads that threw, in all and in a row as the tick counts them: a read that keeps failing ends the names.</summary>
    private static int _failedReads;
    private static int _failedInARow;

    /// <summary>A run loading or tearing down may fail a read or two; this many in a row (five seconds) is a game member that changed.</summary>
    private const int MaxFailedReads = 20;

    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);

    /// <summary>One player of the session as this copy's own game state has it.</summary>
    private readonly record struct Seat(ulong NetId, CharacterModel? Character);

    /// <summary>The local co-op session this copy is in. Host is the NetId of the copy that hosts.</summary>
    private sealed record Session(ulong Local, ulong Host, List<Seat> Seats, string Where);

    /// <summary>What this copy's session makes of a NetId: see <see cref="SeatOf"/>.</summary>
    public enum Seating
    {
        /// <summary>It can't be told right now: joining, loading or tearing down.</summary>
        Unknown,

        /// <summary>This copy is in no local co-op lobby or run: solo, Steam co-op, the menus.</summary>
        NoSession,

        /// <summary>This copy's own player.</summary>
        Local,

        /// <summary>Another player of the session.</summary>
        Other,

        /// <summary>There is a session, and nobody in it has that NetId.</summary>
        Absent,
    }

    /// <summary>
    /// What the human is called: the name the chat box posts their lines under. ModEntry works
    /// that out for the overlay (SPIREMIND_HUMAN_NAME, else the OS user name with a capital)
    /// and hands it to <see cref="Start"/>; until then the same rule is applied here. Both
    /// copies run under one user on one Mac, so a bot's copy arrives at the same name.
    /// </summary>
    public static string HumanName => _human ??= DefaultHumanName();

    /// <summary>True for a bot's copy, as <see cref="Start"/> was told. A copy that was never told counts as the human's.</summary>
    public static bool IsBotCopy => _botCopy == true;

    /// <summary>
    /// Says which copy this is, once at mod load on the main thread: <paramref name="isBot"/>
    /// from ModEntry's InstanceRole (the one place the role is worked out), and the name the
    /// chat box calls the human. Changes nothing by itself: names appear only in a local
    /// co-op lobby or run.
    /// </summary>
    public static void Start(bool isBot, string? humanName)
    {
        _botCopy = isBot;
        if (!string.IsNullOrWhiteSpace(humanName)) _human = humanName.Trim();
        ModEntry.Log($"coop: names: this copy is {(isBot ? "a bot's" : "the human's")}; in a local co-op game the human shows as {HumanName} and each bot as its character");
    }

    /// <summary>The name this copy shows for a player, or null to leave the game's own. Any thread.</summary>
    public static string? NameOf(ulong netId) => _names.TryGetValue(netId, out var name) ? name : null;

    /// <summary>The character a player of this session picked or plays, when this copy can see it. Main thread.</summary>
    public static CharacterModel? CharacterOf(ulong netId) => _characters.GetValueOrDefault(netId);

    /// <summary>
    /// What this copy's session makes of a NetId at this moment. Read from the game afresh,
    /// not from the last tick, and it works after the names were switched off, so the
    /// bubbles' guard never rests on stale state. Never throws. Main thread.
    /// </summary>
    public static Seating SeatOf(ulong netId)
    {
        var session = Read(out var known);
        if (session is null) return known ? Seating.NoSession : Seating.Unknown;
        if (netId == session.Local) return Seating.Local;
        foreach (var seat in session.Seats)
            if (seat.NetId == netId) return Seating.Other;
        return Seating.Absent;
    }

    /// <summary>True when this copy is plainly in no local co-op lobby or run. Read afresh; main thread.</summary>
    public static bool NoSession()
    {
        var session = Read(out var known);
        return session is null && known;
    }

    /// <summary>
    /// The name a bot answers to on that character, as Identity works it out for the local
    /// one: "Ironclad", or the title without "The " for a character it has no name for.
    /// </summary>
    public static string CharacterName(CharacterModel? character)
    {
        if (character is null) return "";
        var id = character.Id.Entry ?? "";
        foreach (var known in Known)
            if (known.Equals(id, StringComparison.OrdinalIgnoreCase)) return known;
        var title = Describe.SafeText(() => character.Title);
        return (title.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? title[4..] : title).Trim();
    }

    /// <summary>
    /// Every frame on the main thread: reads the session a few times a second, publishes the
    /// names when they changed and puts them on the two labels the game writes only once.
    /// A failure is logged once and switches the names off; the game's own come back.
    /// </summary>
    public static void Tick(DateTime now)
    {
        if (_off) return;
        try
        {
            if (now < _next) return;
            _next = now + Interval;

            var failedBefore = _failedReads;
            var session = Read(out var known);
            if (_failedReads == failedBefore) _failedInARow = 0;
            else if (++_failedInARow >= MaxFailedReads)
                throw new InvalidOperationException($"the session couldn't be read {MaxFailedReads} times in a row (the first failure is logged above)");
            if (!known) return; // joining, loading or tearing down: keep what we have
            if (session is not null && !_hooked)
            {
                // Raised on every copy as a run launches, just before its UI is built, so the
                // run's nameplates are right the first time they are drawn. Hooked at the
                // first local co-op lobby this copy is in (a run always starts from one), so
                // a copy that only ever plays solo never has a handler of ours in its run start.
                RunManager.Instance.RunStarted += OnRunStarted;
                _hooked = true;
                ModEntry.Log("coop: names: first local co-op session seen, listening for run starts");
            }
            Publish(session);
            if (session is not null) Relabel();
        }
        catch (Exception ex)
        {
            SwitchOff(ex);
        }
    }

    // ---------- the session ----------

    /// <summary>
    /// The local co-op session this copy is in: a run in progress, else the lobby on top of
    /// the main menu. Null when there is none (solo, Steam co-op, menus). Known is false while
    /// it can't be told (not connected yet, a read failed), and then the names stay as they are.
    /// </summary>
    private static Session? Read(out bool known)
    {
        known = true;
        try
        {
            // The run first: its characters are final (a Random pick is resolved), and the
            // lobby screen may still be up for a moment while the run loads. NetService is
            // not cleared after a run, so it counts only while one is in progress.
            var rm = RunManager.Instance;
            if (rm is { IsInProgress: true, NetService: { } service } && LocalCoop(service))
            {
                if (!service.IsConnected || rm.DebugOnlyGetState() is not { } run)
                {
                    known = false;
                    return null;
                }
                return SessionOf(service, run.Players.Select(p => new Seat(p.NetId, p.Character)), "run");
            }

            // The screens are created with the main menu, so one counts only on top of the
            // stack. A solo character select has a lobby too, of the singleplayer kind.
            switch (NGame.Instance?.MainMenu?.SubmenuStack?.Peek())
            {
                case NCharacterSelectScreen { Lobby: { } lobby }:
                    return OfLobby(lobby, "lobby", out known);

                // A custom-mode lobby (--fastmp host_custom). The daily one keeps its lobby in
                // a private field and is left alone: its rows show the game's names, and a
                // run started from it is still named from its own state.
                case NCustomRunScreen { Lobby: { } lobby }:
                    return OfLobby(lobby, "custom lobby", out known);

                // Continuing a saved co-op run: another screen, whose lobby is a private field
                // (reflected; recheck after a game update). Should the field be gone, only
                // that lobby shows the game's names: the run is named from its own state.
                case NMultiplayerLoadGameScreen load when Nodes.TryGetPrivate(load, "_runLobby") is LoadRunLobby saved && LocalCoop(saved.NetService):
                    if (!saved.NetService.IsConnected) break;
                    return SessionOf(saved.NetService, saved.Run.Players.Select(p => new Seat(p.NetId,
                        p.CharacterId is { } id ? ModelDb.GetByIdOrNull<CharacterModel>(id) : null)), "load lobby");

                default:
                    return null;
            }

            // A lobby whose connection isn't up yet (an ENet client's NetId reads 0 until the host has answered).
            known = false;
            return null;
        }
        catch (Exception ex)
        {
            _failedReads++;
            LogOnce("reading the session", ex);
            known = false;
            return null;
        }
    }

    /// <summary>
    /// The session of a new-run lobby: null for a solo or Steam one, and not known while its
    /// connection isn't up yet. LobbyPlayer is a struct, copied out afresh on every read; a
    /// joiner's character is the Ironclad until its own copy picks.
    /// </summary>
    private static Session? OfLobby(StartRunLobby lobby, string where, out bool known)
    {
        known = true;
        if (!LocalCoop(lobby.NetService)) return null;
        if (!lobby.NetService.IsConnected)
        {
            known = false;
            return null;
        }
        return SessionOf(lobby.NetService, lobby.Players.Select(p => new Seat(p.id, p.character)), where);
    }

    /// <summary>
    /// Host or client of an ENet game. Its platform is None on both sides, also on a copy that
    /// has Steam running, which is what sends the game to NullPlatformUtilStrategy for names.
    /// </summary>
    private static bool LocalCoop(INetGameService? service) =>
        service is { Type: NetGameType.Host or NetGameType.Client, Platform: PlatformType.None };

    private static Session SessionOf(INetGameService service, IEnumerable<Seat> seats, string where)
    {
        // A client knows its host's id (always 1 over ENet); the host is its own.
        var local = service.NetId;
        var host = service is NetClientGameService client ? client.HostNetId : local;
        return new Session(local, host, seats.ToList(), where);
    }

    /// <summary>
    /// The names for a session. The human is this copy's own player, or on a bot's copy the
    /// host; everyone else is named after their character, numbered from the second one on
    /// the same character. A player whose character can't be read keeps the game's name.
    /// </summary>
    private static Dictionary<ulong, string> NamesFor(Session session)
    {
        ulong? human = IsBotCopy ? (session.Host != session.Local ? session.Host : null) : session.Local;
        var names = new Dictionary<ulong, string>();
        var count = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var seat in session.Seats)
        {
            if (seat.NetId == human)
            {
                names[seat.NetId] = HumanName;
                continue;
            }
            var name = CharacterName(seat.Character);
            if (name.Length == 0) continue;
            var nth = count[name] = count.GetValueOrDefault(name) + 1;
            names[seat.NetId] = nth == 1 ? name : $"{name} {nth}";
        }
        return names;
    }

    /// <summary>Swaps in the names for a session (none for null) when they differ from the ones in use, and says so in the log.</summary>
    private static void Publish(Session? session)
    {
        if (session is null)
        {
            _characters = NoCharacters;
            if (_names.Count == 0) return;
            _names = NoNames;
            ModEntry.Log("coop: names: no local co-op session any more, back to the game's own");
            return;
        }

        var characters = new Dictionary<ulong, CharacterModel?>();
        foreach (var seat in session.Seats) characters[seat.NetId] = seat.Character;
        _characters = characters;

        var names = NamesFor(session);
        var current = _names;
        if (names.Count == current.Count && names.All(n => current.TryGetValue(n.Key, out var had) && had == n.Value)) return;
        _names = names;
        var copy = _botCopy switch
        {
            true => "a bot's",
            false => "the human's",
            null => "taken for the human's (RemoteBubbles.Start was never called)",
        };
        ModEntry.Log($"coop: names ({session.Where}, this copy is {copy}, local {session.Local}, host {session.Host}, "
                     + $"name patch {(CoopNamePatch.IsApplied() ? "on" : "NOT on, so only the rows are renamed")}): "
                     + string.Join(", ", names.Select(n => $"{n.Key} is {n.Value}")));
    }

    /// <summary>
    /// RunManager.Launch raises this from inside the run's start, on every copy, for new and
    /// loaded runs, with the players final. It must never throw: an exception here leaves
    /// Launch, and the game takes that for a failed start and goes back to the main menu.
    /// Does nothing for a solo or Steam run.
    /// </summary>
    private static void OnRunStarted(RunState state)
    {
        try
        {
            if (_off || RunManager.Instance.NetService is not { } service || !LocalCoop(service) || !service.IsConnected) return;
            Publish(SessionOf(service, state.Players.Select(p => new Seat(p.NetId, p.Character)), "run start"));
        }
        catch (Exception ex)
        {
            try { LogOnce("naming the players at run start", ex); }
            catch { /* never out of this handler */ }
        }
    }

    // ---------- the labels the game writes once ----------

    /// <summary>
    /// The lobby row and the run's player-list row read their name once, when they are made:
    /// before a bot's name is known when it has only just joined, and never again when it
    /// changes character in the lobby. Whichever of them shows something else is rewritten.
    /// The nameplate under a figure in a fight, the expanded player view and the hover texts
    /// ask again every time, so the patch alone covers them.
    /// </summary>
    private static void Relabel()
    {
        if (_relabelOff) return;
        try
        {
            foreach (var row in FindLobbyRows()) Relabel(row, row.PlayerId);
            foreach (var row in FindRunRows()) Relabel(row, row.Player.NetId);
        }
        catch (Exception ex)
        {
            // Not tried again four times a second: the patch still names everything that asks.
            _relabelOff = true;
            ModEntry.LogError($"coop: names: rewriting the rows' nameplates failed and is switched off; a row may keep the game's name: {ex}");
        }
    }

    private static void Relabel(Node row, ulong netId)
    {
        // A row not ready yet writes its own label in a moment, through the patch.
        if (NameOf(netId) is not { } name || !row.IsNodeReady()) return;
        if (Nodes.Get<MegaLabel>(row, "%NameplateLabel") is not { } label || label.Text == name) return;
        label.SetTextAutoSize(name);
        ModEntry.Log($"coop: names: {row.Name} of {netId} now reads {name}");
    }

    /// <summary>
    /// The lobby's player rows on this screen: at character select every player has one, the
    /// local one too; when continuing a saved run only the others. Rows on their way out are
    /// skipped: a lobby screen that closes keeps its old rows until the end of that frame.
    /// </summary>
    public static List<NRemoteLobbyPlayer> LobbyRows()
    {
        try
        {
            return FindLobbyRows();
        }
        catch (Exception ex)
        {
            LogOnce("finding the lobby rows", ex);
            return [];
        }
    }

    /// <summary>The run's player list on the left: one row per player in co-op (the local one first), none in a solo run.</summary>
    public static List<NMultiplayerPlayerState> RunRows()
    {
        try
        {
            return FindRunRows();
        }
        catch (Exception ex)
        {
            LogOnce("finding the player rows", ex);
            return [];
        }
    }

    private static List<NRemoteLobbyPlayer> FindLobbyRows()
    {
        // Each screen finds its row container the way it does itself, and the rows sit in
        // that container's child "Container".
        var top = NGame.Instance?.MainMenu?.SubmenuStack?.Peek();
        var container = top switch
        {
            NCharacterSelectScreen => Nodes.Get<Node>(top, "RemotePlayerContainer"),
            NCustomRunScreen => Nodes.Get<Node>(top, "%RemotePlayerContainer"),
            NMultiplayerLoadGameScreen => Nodes.Get<Node>(top, "RemotePlayerLoadContainer"),
            _ => null,
        };
        if (Nodes.Get<Node>(container, "Container") is not { } list) return [];
        return list.GetChildren().OfType<NRemoteLobbyPlayer>().Where(row => Nodes.Alive(row)).ToList();
    }

    private static List<NMultiplayerPlayerState> FindRunRows()
    {
        if (NRun.Instance?.GlobalUi?.MultiplayerPlayerContainer is not { } list || !Nodes.Alive(list)) return [];
        return list.GetChildren().OfType<NMultiplayerPlayerState>().Where(row => Nodes.Alive(row) && row.Player is not null).ToList();
    }

    // ---------- plumbing ----------

    /// <summary>ModEntry.ResolveHumanName's rule, for when <see cref="Start"/> was given no name.</summary>
    private static string DefaultHumanName()
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

    private static void SwitchOff(Exception ex)
    {
        _off = true;
        _names = NoNames;
        _characters = NoCharacters;
        ModEntry.LogError($"coop: names switched off, the game's own are back: {ex}");
        try
        {
            if (_hooked) RunManager.Instance.RunStarted -= OnRunStarted;
        }
        catch
        {
            // The handler does nothing once switched off.
        }
    }

    private static void LogOnce(string what, Exception ex)
    {
        if (Logged.Add(what)) ModEntry.LogError($"coop: names: {what}: {ex.Message}");
    }
}

/// <summary>
/// The one place a local game makes a player's name: NullPlatformUtilStrategy.GetPlayerName,
/// reached through PlatformUtil.GetPlayerName and GetPlayerNameRaw whenever the session's
/// platform is None. That is a local (ENet) co-op game on both of its copies, also when the
/// host has Steam running. Lobby rows, the run's player list, the nameplate under a figure,
/// the expanded player view and the hover texts that name a player all end up here.
///
/// The postfix is a dictionary read and nothing else: the game calls this on every combat
/// state change, and may call it off the main thread. With no local co-op session
/// <see cref="CoopNames"/> has no names and the game's answer stands, so a solo game, Steam
/// co-op and the run history are untouched. Applied in every copy; display only.
/// </summary>
[HarmonyPatch]
internal static class CoopNamePatch
{
    private static bool _failed;
    private static bool _answered;

    private static MethodBase? Target() =>
        AccessTools.DeclaredMethod(typeof(NullPlatformUtilStrategy), nameof(NullPlatformUtilStrategy.GetPlayerName), [typeof(ulong)]);

    private static MethodBase TargetMethod() =>
        Target() ?? throw new MissingMethodException(nameof(NullPlatformUtilStrategy), nameof(NullPlatformUtilStrategy.GetPlayerName));

    /// <summary>True when the postfix is on the game's method, for the log: without it only the rows CoopNames rewrites are renamed.</summary>
    public static bool IsApplied()
    {
        try
        {
            return Target() is { } target
                   && HarmonyLib.Harmony.GetPatchInfo(target)?.Postfixes.Any(p => p.PatchMethod.DeclaringType == typeof(CoopNamePatch)) == true;
        }
        catch
        {
            return false;
        }
    }

    private static void Postfix(ulong __0, ref string __result)
    {
        try
        {
            if (CoopNames.NameOf(__0) is not { } name) return;
            __result = name;
            if (_answered) return;
            // Once, so the first co-op test's log shows the game really asks here.
            _answered = true;
            ModEntry.Log($"coop: names: the game asked for player {__0}'s name and got {name}");
        }
        catch (Exception ex)
        {
            // The game's own name stands.
            if (_failed) return;
            _failed = true;
            ModEntry.LogError($"coop: names: the name patch failed: {ex.Message}");
        }
    }
}
