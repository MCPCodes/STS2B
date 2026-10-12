using System.Globalization;
using HarmonyLib;
using SpireMind.Patches;
// Aliases rather than the namespace, so a game type of the same name in the global usings can
// never make them ambiguous.
using GuestArgs = SpireMind.Core.Coop.GuestArgs;
using GuestAdvice = SpireMind.Core.Coop.GuestAdvice;

namespace SpireMind.Game.Coop;

/// <summary>
/// Everything the mod does in a guest copy: a friend's own game, on another computer, that
/// joins the host's local co-op lobby (docs/coop-guest.md). ModEntry starts this and nothing
/// else there, so a guest copy has no bridge, no brain, no chat box, no relay and no way to
/// start a bot copy. It writes no file and starts no process. What it does:
/// <list type="bullet">
/// <item>The join. With <c>--fastmp=join</c> the game's Join screen dials 127.0.0.1:33771
/// through a private method, NJoinFriendScreen.FastMpJoin. A prefix on it makes the same
/// call the game makes, the public NJoinFriendScreen.JoinGameAsync with the game's own
/// ENetClientConnectionInitializer, with the host's address in place of 127.0.0.1. Everything
/// after that is the game's code: one attempt each time the Join screen opens, the game's own
/// popups, Back and Join to try again. The player id is read from <c>--clientId</c> the way
/// the game reads it, because the game also takes that number for "me" when it books the
/// run's result.</item>
/// <item>The names. The game never sends names, so this copy would show "Test Host" and
/// "Test Client 2". <see cref="CoopNames"/> shows the guest's own name, the host's name and
/// each bot's character instead. Display only.</item>
/// <item>The popups. The game's "Disconnected" popup gets one more line: where this copy
/// dialed and what to do (<see cref="GuestAdvice"/>). When the launch options can't be used,
/// one popup on the main menu says what is wrong, and nothing is dialed.</item>
/// <item>The guest's own saved co-op run. With <c>--fastmp</c> set, Load in the Multiplayer
/// menu looks for the <c>--clientId</c> player in the save, does not find it in a run the guest
/// hosted over Steam, and renames that save to ".corrupt". A prefix keeps Load from running
/// while the option is set and says why.</item>
/// </list>
/// Every entry point catches, logs one line starting with "guest:" and leaves the game as it
/// is. The patches are applied by hand, here only, so the host's copy and the bot copies run
/// the game's own join untouched. Main thread.
/// </summary>
internal static class GuestMode
{
    private const string HarmonyId = "mcpcodes.spiremind.guest";

    /// <summary>The window title the game's own fast join sets, kept so a guest copy looks as the game's client does.</summary>
    private const string ClientTitle = "Slay The Spire 2 (Client)";

    /// <summary>The main menu must have been up this long before the problems popup is shown, so it does not race the menu's own first popups.</summary>
    private static readonly TimeSpan MenuSettle = TimeSpan.FromSeconds(1.5);

    private static GuestArgs? _args;
    private static bool _started;
    private static bool _joinPatched;
    private static int _attempts;

    private static bool _tickFailed;
    private static bool _dialFailed;
    private static bool _popupFailed;
    private static bool _problemsFailed;
    private static bool _loadFailed;

    /// <summary>Load was pressed and kept from running: the frame tick owes the player the popup that says why.</summary>
    private static bool _loadNoteDue;

    /// <summary>The problems popup is owed: a new main menu came up, or the Join screen was opened. The tick shows it.</summary>
    private static bool _problemsDue;
    private static ulong _menuSeen;
    private static DateTime _menuSince;

    private static System.Reflection.FieldInfo? _bodyField;
    private static bool _bodyLooked;

    /// <summary>True in a guest copy, once <see cref="Start"/> has run.</summary>
    public static bool Active => _args is not null;

    /// <summary>
    /// Call once from the mod's initializer, on the main thread, in a guest copy only. Never
    /// throws: a part that fails is logged and left off, and the game stays playable.
    /// </summary>
    /// <param name="args">The guest's launch options, as the role detection read them.</param>
    /// <param name="ownName">What this copy calls its own player when the options give no name.</param>
    public static void Start(GuestArgs args, string ownName)
    {
        if (_started || args is null) return;
        _started = true;
        _args = args;

        try
        {
            foreach (var problem in args.Problems)
                ModEntry.LogError($"guest: launch options: {problem}");
            if (!args.CanDial)
                ModEntry.LogError($"guest: nothing will be dialed until the launch options are right. They should look like: {GuestArgs.Example}");
        }
        catch
        {
            // The log is all that can fail here.
        }

        var harmony = new Harmony(HarmonyId);
        Part("the names", () =>
        {
            CoopNames.StartGuest(args.Name ?? ownName, args.HostName);
            harmony.CreateClassProcessor(typeof(CoopNamePatch)).Patch();
        });
        Part("the join to the host's address", () => PatchJoin(harmony));
        Part("the line on the game's network error popup", () => PatchPopup(harmony));
        Part("the guard on this player's own saved co-op run", () => PatchLoad(harmony));
        Part("the frame tick", () =>
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(Tick));
        });

        ModEntry.Log(_joinPatched && args.CanDial
            ? $"guest: ready: the Join screen dials {args.Address}:{args.Port} as player {args.ClientId}"
            : "guest: NOT ready to join: see the guest: lines above");
    }

    private static void Part(string what, Action start)
    {
        try { start(); }
        catch (Exception ex)
        {
            ModEntry.LogError($"guest: starting {what} failed, so that part is off (the game itself is untouched): {ex}");
        }
    }

    // ---------- the join ----------

    private static void PatchJoin(Harmony harmony)
    {
        // Private: the one place the game's fast join names its address. Recheck after a game update.
        var target = AccessTools.DeclaredMethod(typeof(NJoinFriendScreen), "FastMpJoin", Type.EmptyTypes)
            ?? throw new MissingMethodException(nameof(NJoinFriendScreen), "FastMpJoin");
        if (target.ReturnType != typeof(Task))
            throw new InvalidOperationException($"NJoinFriendScreen.FastMpJoin returns {target.ReturnType.Name}, not Task, in this build");
        harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(GuestMode), nameof(BeforeFastMpJoin))));
        _joinPatched = true;
        ModEntry.Log("guest: harmony: the game's fast join now takes the host's address from the launch options");
    }

    /// <summary>
    /// Prefix on NJoinFriendScreen.FastMpJoin, which the Join screen runs each time it opens
    /// when <c>--fastmp</c> is set. The game's method builds an ENet initializer for
    /// 127.0.0.1:33771 and hands it to its public JoinGameAsync; this does the same with the
    /// host's address and skips the original. The caller wraps the returned task exactly as it
    /// wraps the original's. With launch options that can't be used nothing is dialed and a
    /// popup says why. If anything here throws, the game's own method runs (it dials this
    /// computer and times out, as without the mod).
    /// </summary>
    private static bool BeforeFastMpJoin(NJoinFriendScreen __instance, ref Task __result)
    {
        try
        {
            if (_args is not { } args) return true;

            if (!args.CanDial)
            {
                ModEntry.LogError("guest: the Join screen opened, but the launch options can't be used, so nothing is dialed");
                // Shown by the frame tick, not from inside the menu's own set-up.
                _problemsDue = true;
                __result = Task.CompletedTask;
                return false;
            }

            // The id as the game itself reads it: it must be the very number the game takes for
            // "me" at the end of the run (its no-platform player id, also from --clientId).
            if (!CommandLineHelper.TryGetValue(GuestArgs.ClientIdFlag, out var raw)
                || !ulong.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var netId)
                || netId != args.ClientId)
            {
                ModEntry.LogError($"guest: the game reads --{GuestArgs.ClientIdFlag} as '{raw}', which is not the player id {args.ClientId} the mod read, so nothing is dialed");
                __result = Task.CompletedTask;
                return false;
            }

            var attempt = ++_attempts;
            ModEntry.Log($"guest: join attempt {attempt}: dialing {args.Address}:{args.Port} as player {netId} (the game's own join, with the host's address in place of 127.0.0.1)");
            try { DisplayServer.WindowSetTitle(ClientTitle); }
            catch { /* the title is decoration */ }

            __result = __instance.JoinGameAsync(new ENetClientConnectionInitializer(netId, args.Address!, (ushort)args.Port));
            return false;
        }
        catch (Exception ex)
        {
            if (!_dialFailed)
            {
                _dialFailed = true;
                ModEntry.LogError($"guest: dialing the host's address failed, so the game's own join runs (it dials this computer and will time out): {ex}");
            }
            return true;
        }
    }

    // ---------- the game's network error popup ----------

    private static void PatchPopup(Harmony harmony)
    {
        // Three Create overloads; every network failure the game reports goes through this one.
        var create = AccessTools.DeclaredMethod(typeof(NErrorPopup), nameof(NErrorPopup.Create), [typeof(NetErrorInfo)])
            ?? throw new MissingMethodException(nameof(NErrorPopup), "Create(NetErrorInfo)");
        harmony.Patch(create, postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(GuestMode), nameof(ErrorPopupCreated))));
    }

    /// <summary>
    /// Postfix on NErrorPopup.Create(NetErrorInfo): the game is about to show why a join failed
    /// or a session ended. The reason goes to the log, and one line is added to the popup's
    /// text before it is drawn: where this copy dialed and what to do. Changes nothing else.
    /// </summary>
    private static void ErrorPopupCreated(NetErrorInfo __0, NErrorPopup? __result)
    {
        try
        {
            if (__result is null || _args is not { } args) return;

            var reason = "";
            var detail = "";
            // Both throw for an info struct that holds no error at all.
            try { reason = __0.GetReason().ToString(); } catch { }
            try { detail = __0.GetErrorString() ?? ""; } catch { }
            // Without the hook on the game's join, the game dialed this computer, whatever the options say.
            var line = _joinPatched
                ? GuestAdvice.ForPopup(reason, args)
                : $"{GuestAdvice.Lead} the mod could not set the address in this version of the game, so the game looked for a host on this computer. Tell {args.HostLabel}; the log has the reason.";
            ModEntry.Log($"guest: the game reports: {(reason.Length == 0 ? "a network error" : reason)}{(detail.Length == 0 ? "" : $" ({OneLine(detail)})")}");
            ModEntry.Log($"guest: {line}");

            // The popup reads this private field when it enters the tree, which is after this.
            if (!_bodyLooked)
            {
                _bodyLooked = true;
                _bodyField = AccessTools.Field(typeof(NErrorPopup), "_body");
                if (_bodyField is null || _bodyField.FieldType != typeof(string))
                {
                    _bodyField = null;
                    ModEntry.LogError("guest: the error popup's text can't be reached in this build, so the SpireMind line is in the log only");
                }
            }
            if (_bodyField?.GetValue(__result) is string body)
                _bodyField.SetValue(__result, $"{body}\n\n{line}");
        }
        catch (Exception ex)
        {
            if (_popupFailed) return;
            _popupFailed = true;
            ModEntry.LogError($"guest: adding to the network error popup failed (the game's own text stands): {ex.Message}");
        }
    }

    private static string OneLine(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 400 ? flat : flat[..400] + "...";
    }

    // ---------- the guest's own saved co-op run ----------

    private static void PatchLoad(Harmony harmony)
    {
        // Private: the Multiplayer menu's Load button. Recheck after a game update.
        var load = AccessTools.DeclaredMethod(typeof(NMultiplayerSubmenu), "StartLoad", [typeof(NButton)])
            ?? throw new MissingMethodException(nameof(NMultiplayerSubmenu), "StartLoad(NButton)");
        harmony.Patch(load, prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(GuestMode), nameof(BeforeLoad))));
    }

    /// <summary>
    /// Prefix on NMultiplayerSubmenu.StartLoad. While <c>--fastmp</c> is on the command line the
    /// game reads a saved co-op run as the <c>--clientId</c> player. A run this player hosted
    /// over Steam does not hold that id, so the game takes the save for broken and renames it.
    /// In a guest copy Load therefore does nothing but say so; without the option the game's
    /// own Load runs.
    /// </summary>
    private static bool BeforeLoad()
    {
        var risky = true;
        try
        {
            if (_args is null) return true;
            risky = CommandLineHelper.HasArg(GuestArgs.FastMpFlag);
            if (!risky) return true;
            ModEntry.Log("guest: Load was pressed in the Multiplayer menu; not run, because with --fastmp set the game would rename this player's own saved co-op run to .corrupt");
            // Shown by the frame tick, not from inside the button's own signal.
            _loadNoteDue = true;
            return false;
        }
        catch (Exception ex)
        {
            if (!_loadFailed)
            {
                _loadFailed = true;
                ModEntry.LogError($"guest: guarding Load failed: {ex.Message}");
            }
            // With the option set, not loading is the safe side.
            return !risky;
        }
    }

    /// <summary>The popup that says why Load did nothing. False while another popup is open.</summary>
    private static bool ShowLoadNote(GuestArgs args)
    {
        if (_problemsFailed) return true;
        try
        {
            if (NModalContainer.Instance is not { } modals || modals.OpenModal is not null) return false;
            var body = $"{GuestAdvice.Lead} Load is switched off while this game is set up to join {args.HostLabel}.\n\n"
                       + "With these Launch Options the game would not find you in your own saved co-op run and would set the save aside as broken. "
                       + "Your save is untouched. To load it: quit, remove the Launch Options in Steam, and start the game again.";
            if (NErrorPopup.Create("SpireMind", body, showReportBugButton: false) is { } popup)
                modals.Add(popup);
            return true;
        }
        catch (Exception ex)
        {
            _problemsFailed = true;
            ModEntry.LogError($"guest: showing why Load is off failed (the log has it): {ex.Message}");
            return true;
        }
    }

    // ---------- launch options that can't be used ----------

    /// <summary>
    /// Every frame on the main thread: keeps the names current, and with launch options that
    /// can't be used shows the popup that says so once the main menu has settled. A failure is
    /// logged once and ends the tick; the game is untouched.
    /// </summary>
    private static void Tick()
    {
        if (_tickFailed) return;
        try
        {
            var now = DateTime.UtcNow;
            CoopNames.Tick(now); // guards itself

            if (_loadNoteDue && _args is { } guest && ShowLoadNote(guest)) _loadNoteDue = false;

            if (_args is not { CanDial: false } args) return;
            if (NGame.Instance?.MainMenu is not { } menu || !menu.IsNodeReady()) return;
            var id = menu.GetInstanceId();
            if (id != _menuSeen)
            {
                _menuSeen = id;
                _menuSince = now;
                _problemsDue = true;
            }
            if (!_problemsDue || now - _menuSince < MenuSettle) return;
            if (ShowProblems(args)) _problemsDue = false;
        }
        catch (Exception ex)
        {
            _tickFailed = true;
            ModEntry.LogError($"guest: the frame tick failed and is off (the join itself is not affected): {ex}");
        }
    }

    /// <summary>
    /// One popup, the game's own, that says what is wrong with the launch options: once for
    /// each main menu and each time the Join screen is opened. False while another popup is
    /// open, and then the tick tries again.
    /// </summary>
    private static bool ShowProblems(GuestArgs args)
    {
        if (_problemsFailed) return true;
        try
        {
            if (NModalContainer.Instance is not { } modals || modals.OpenModal is not null) return false;

            var problems = args.Problems.Count > 0
                ? string.Join("\n", args.Problems.Select(p => $"- {p}"))
                : "- The launch options are not complete.";
            var body = $"{GuestAdvice.Lead} this game can't join {args.HostLabel} yet.\n{problems}\n\n"
                       + "Quit the game, fix the Launch Options in Steam (right-click the game, Properties), and start it again.";
            if (NErrorPopup.Create("SpireMind", body, showReportBugButton: false) is not { } popup) return true;
            modals.Add(popup);
            ModEntry.Log("guest: showed the popup that lists what is wrong with the launch options");
            return true;
        }
        catch (Exception ex)
        {
            _problemsFailed = true;
            ModEntry.LogError($"guest: showing the launch options' problems failed (they are in the log above): {ex.Message}");
            return true;
        }
    }
}
