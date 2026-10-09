using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// The end of a run (NGameOverScreen on the overlay stack): win, loss or abandon, where it
/// ended, the score and the final build, so the bot can write its post-mortem before leaving.
/// The only action is "continue", one click on whichever of the screen's buttons is shown and
/// enabled: continue (to the run summary), then on a daily run the leaderboard button (also an
/// NGameOverContinueButton), then return to the main menu. A disabled one is also hidden.
/// </summary>
internal sealed class GameOverReader : IScreen
{
    public static readonly GameOverReader Instance = new();

    public string Name => "game_over";

    /// <summary>The screen fades in over the last room; don't offer a click before it has.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(800);

    /// <summary>How often Capture may walk the screen's tree while no button is shown.</summary>
    private static readonly TimeSpan Rescan = TimeSpan.FromMilliseconds(500);

    /// <summary>No continue button after this long gets logged once, with the buttons that are there.</summary>
    private static readonly TimeSpan NoButtonLog = TimeSpan.FromSeconds(8);

    /// <summary>Continuing can run the run's clean-up and a transition before the menu shows.</summary>
    private static readonly TimeSpan ContinueTimeout = TimeSpan.FromSeconds(10);

    private GameOverReader() { }

    // ---------- per-screen memory (reset when a different game over screen shows up) ----------

    private static ulong _screenId;
    private static DateTime _firstSeen;
    private static DateTime _lastScan;
    private static NClickableControl? _button;
    private static bool _loggedNoButton;

    private static void Track(NGameOverScreen screen)
    {
        var id = screen.GetInstanceId();
        if (id == _screenId) return;
        _screenId = id;
        _firstSeen = DateTime.UtcNow;
        _lastScan = DateTime.MinValue;
        _button = null;
        _loggedNoButton = false;
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (node is not NGameOverScreen screen || !Nodes.Alive(screen))
            return GameSnapshot.Busy(Name, "game over screen closing");

        Track(screen);
        var now = DateTime.UtcNow;
        Refresh(screen, now, force: false);
        var state = BuildState(ctx);

        string? waiting = null;
        if (now - _firstSeen < Settle)
            waiting = "game over screen appearing";
        else if (_button is { } clicked && Nodes.Alive(clicked) && Clicks.RecentlyClicked(clicked))
            waiting = "leaving the game over screen";
        else if (!MiscUi.Usable(_button))
            waiting = NoButtonReason(screen, now);

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        return new GameSnapshot
        {
            Screen = Name,
            AwaitingDecision = true,
            State = state,
            Actions = [new LegalAction("continue", _button is NReturnToMainMenuButton ? "Return to the main menu" : "Continue to the next page of the game over screen")],
        };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (actionId != "continue")
            return ActionResult.Fail($"Unknown game over action '{actionId}'.");
        if (node is not NGameOverScreen screen || !Nodes.Alive(screen))
            return ActionResult.Fail("The game over screen is gone.");

        Track(screen);
        if (DateTime.UtcNow - _firstSeen < Settle)
            return ActionResult.Fail("The game over screen is still appearing.");

        Refresh(screen, DateTime.UtcNow, force: true);
        if (_button is not { } button)
            return ActionResult.Fail("There is no continue button.");
        if (!MiscUi.Usable(button))
            return ActionResult.Fail("The continue button is not enabled yet.");

        // MenuReader hands us the screen when the run is already flagged over, so "left the run"
        // and "no longer the top overlay" only count when they held at the click.
        var wasInRun = ctx.InRun;
        var wasTop = ReferenceEquals(Nodes.TopOverlay(), screen);
        var click = Clicks.Once(button, () => button.ForceClick(), "the continue button");
        if (!click.Ok)
            return click;

        // Done when the screen (or this stage of it) is gone, the button went inactive, or the
        // run has been torn down.
        ActionTracker.Begin(
            "leaving the game over screen",
            c => !Nodes.Alive(screen)
                || !Nodes.Alive(button)
                || !Nodes.IsShown(button)
                || !button.IsEnabled
                || (wasTop && !ReferenceEquals(Nodes.TopOverlay(), screen))
                || (wasInRun && !c.InRun)
                || Nodes.ModalOpen(),
            ContinueTimeout);
        return ActionResult.Success;
    }

    // ---------- reading ----------

    private static JsonObject BuildState(GameContext ctx)
    {
        var rs = ctx.Run;
        var rm = RunManager.Instance;
        var result = rs?.CurrentRoom?.IsVictoryRoom == true ? "victory"
            : rm?.IsAbandoned == true ? "abandoned"
            : rs is not null ? "defeat"
            : "unknown";

        var state = new JsonObject { ["result"] = result };
        if (rs is not null)
        {
            state["run"] = Describe.RunInfo(rs);
            if (rs.CurrentRoom is { } room)
                state["room"] = room.RoomType.ToString();

            // The screen computes the same number from its saved copy of the run.
            try { state["score"] = ScoreUtility.CalculateScore(rs, result == "victory"); }
            catch { /* the score is a nicety; the rest of the snapshot still stands */ }
        }

        if (ctx.Me is { } me)
            state["player"] = Describe.PlayerRun(me, includeDeck: true);
        return state;
    }

    /// <summary>
    /// Finds the button to click with one walk of the screen, at most every <see cref="Rescan"/>,
    /// and only while none is shown (each stage hides its button and shows the next one's).
    /// <paramref name="force"/> walks now.
    /// </summary>
    private static void Refresh(NGameOverScreen screen, DateTime now, bool force)
    {
        if (!force && _button is { } b && Nodes.Alive(b) && Nodes.IsShown(b)) return;
        if (!force && now - _lastScan < Rescan) return;
        _lastScan = now;

        NClickableControl? shown = null;
        NClickableControl? hidden = null;
        foreach (var button in Nodes.FindAll<NClickableControl>(screen))
        {
            if (button is not (NGameOverContinueButton or NReturnToMainMenuButton)) continue;
            if (Nodes.IsShown(button)) shown ??= button;
            else hidden ??= button;
        }

        _button = shown ?? hidden;
    }

    private static string NoButtonReason(NGameOverScreen screen, DateTime now)
    {
        if (_button is not null)
            return "waiting for the continue button to enable";

        if (!_loggedNoButton && now - _firstSeen > NoButtonLog)
        {
            _loggedNoButton = true;
            var seen = Nodes.FindAll<NClickableControl>(screen)
                .Select(c => $"{c.GetType().Name}({c.Name})")
                .Distinct()
                .Take(12);
            ModEntry.LogError($"game over: no continue button found; buttons here: {string.Join(", ", seen)}");
        }
        return "waiting for the continue button";
    }
}

/// <summary>
/// A popup in NModalContainer, which sits above every other screen. Tutorial (FTUE) popups
/// get "dismiss", one click on their confirm button (the three-page combat rules tutorial has
/// a next arrow instead, clicked once per page). Everything else (error popups, the
/// mod-loading consent prompt whose Yes saves and quits the game, the abandon-run
/// confirmation, the "enable tutorials?" choice, anything unknown) is left for Mark: no
/// actions, and a wait that names the popup's class.
/// </summary>
internal sealed class ModalReader : IScreen
{
    public static readonly ModalReader Instance = new();

    public string Name => "modal";

    /// <summary>Popups animate in; the confirm button can be live before the popup is readable.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    /// <summary>A tutorial popup with no confirm button after this long needs Mark.</summary>
    private static readonly TimeSpan NoButtonGrace = TimeSpan.FromSeconds(2);

    /// <summary>A confirm button still hidden or disabled after this long needs Mark.</summary>
    private static readonly TimeSpan NotClickableGrace = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan DismissTimeout = TimeSpan.FromSeconds(4);

    /// <summary>Clicks on one page of a popup (same text) before giving up and asking Mark.</summary>
    private const int MaxDismissClicks = 2;

    private const int MaxTextChars = 300;

    /// <summary>
    /// Node name of the tutorials' confirm button (each NFtue gets it by this name, typed only
    /// as NButton, so the name backs up the NFtueConfirmButton class check).
    /// </summary>
    private const string FtueConfirmName = "FtueConfirmButton";

    /// <summary>NCombatRulesFtue's next-page arrow; on the last page it closes the tutorial.</summary>
    private const string RulesNextName = "RightArrow";

    /// <summary>
    /// Never clicked, whatever buttons they hold: matched against the popup's class name.
    /// "ModLoading" is NConfirmModLoadingPopup, whose Yes saves and quits the game;
    /// "AcceptTutorials" is the yes/no on turning tutorials off, a setting for Mark.
    /// </summary>
    private static readonly string[] NeverTouch = ["ModLoading", "ModConsent", "Error", "Abandon", "Quit", "Disconnect", "AcceptTutorials"];

    private ModalReader() { }

    // ---------- per-popup memory (reset when a different popup shows up) ----------

    private static ulong _modalId;
    private static DateTime _firstSeen;
    private static int _dismissClicks;
    private static string _textAtClick = "";
    private static bool _loggedNoButton;

    private static void Track(Node modal)
    {
        var id = modal.GetInstanceId();
        if (id == _modalId) return;
        _modalId = id;
        _firstSeen = DateTime.UtcNow;
        _dismissClicks = 0;
        _textAtClick = "";
        _loggedNoButton = false;
        ModEntry.Log($"popup up: {modal.GetType().Name}");
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (Modal(node) is not { } modal)
            return GameSnapshot.Busy(Name, "popup closing");

        Track(modal);
        var now = DateTime.UtcNow;
        var type = modal.GetType().Name;
        var tutorial = IsTutorial(modal);

        var state = new JsonObject
        {
            ["popup"] = type,
            ["kind"] = tutorial ? "tutorial" : "other",
        };

        if (now - _firstSeen < Settle)
            return Wait(state, tutorial ? "tutorial popup appearing" : $"a popup needs Mark: {type}");

        var text = MiscUi.Cap(MiscUi.VisibleText(modal), MaxTextChars);
        if (text.Length > 0) state["text"] = text;

        if (!tutorial)
            return Wait(state, $"a popup needs Mark: {type}");

        // A new page of a multi-page tutorial is a new chance to click.
        if (_dismissClicks > 0 && text != _textAtClick)
            _dismissClicks = 0;

        var button = ConfirmButton(modal);
        if (button is not null && Clicks.RecentlyClicked(button))
            return Wait(state, "dismissing the tutorial popup");
        if (_dismissClicks >= MaxDismissClicks)
            return Wait(state, $"a popup needs Mark: {type} (dismissing it did nothing)");

        if (button is null)
        {
            if (now - _firstSeen < NoButtonGrace)
                return Wait(state, "tutorial popup appearing");
            LogNoButton(modal);
            return Wait(state, $"a popup needs Mark: {type} (no confirm button found)");
        }
        if (!MiscUi.Usable(button))
        {
            // A confirm button that never becomes clickable would otherwise read as "appearing" forever.
            return Wait(state, now - _firstSeen < NotClickableGrace
                ? "tutorial popup appearing"
                : $"a popup needs Mark: {type} (its confirm button is not clickable)");
        }

        return new GameSnapshot
        {
            Screen = Name,
            AwaitingDecision = true,
            State = state,
            Actions = [new LegalAction("dismiss", "Dismiss the tutorial popup")],
        };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (actionId != "dismiss")
            return ActionResult.Fail($"Unknown popup action '{actionId}'.");
        if (Modal(node) is not { } modal)
            return ActionResult.Fail("The popup is gone.");

        Track(modal);
        if (!IsTutorial(modal))
            return ActionResult.Fail($"{modal.GetType().Name} is for Mark to answer.");
        if (DateTime.UtcNow - _firstSeen < Settle)
            return ActionResult.Fail("The popup is still appearing.");
        if (_dismissClicks >= MaxDismissClicks)
            return ActionResult.Fail("Dismissing this popup did nothing; Mark needs to close it.");

        if (ConfirmButton(modal) is not { } button)
            return ActionResult.Fail("Can't find the popup's confirm button.");
        if (!MiscUi.Usable(button))
            return ActionResult.Fail("The confirm button is not enabled yet.");

        var text = MiscUi.Cap(MiscUi.VisibleText(modal), MaxTextChars);
        var click = Clicks.Once(button, () =>
        {
            _dismissClicks++;
            _textAtClick = text;
            button.ForceClick();
        }, "the tutorial popup's confirm button");
        if (!click.Ok)
            return click;

        // Done when the popup closed, or (multi-page tutorials) the button went away or inactive or
        // the page text changed.
        ActionTracker.Begin(
            "dismissing the tutorial popup",
            _ => !Nodes.Alive(modal)
                || !ReferenceEquals(NModalContainer.Instance?.OpenModal, modal)
                || !Nodes.Alive(button)
                || !Nodes.IsShown(button)
                || !button.IsEnabled
                || MiscUi.Cap(MiscUi.VisibleText(modal), MaxTextChars) != text,
            DismissTimeout);
        return ActionResult.Success;
    }

    // ---------- reading ----------

    private static GameSnapshot Wait(JsonObject state, string why) =>
        new() { Screen = "modal", AwaitingDecision = false, Waiting = why, State = state };

    /// <summary>The router's modal, as long as it is still the one the container shows.</summary>
    private static Node? Modal(Node? node)
    {
        if (node is null || !Nodes.Alive(node)) return null;
        return ReferenceEquals(NModalContainer.Instance?.OpenModal, node) ? node : null;
    }

    private static bool NeverClick(Node modal)
    {
        var type = modal.GetType().Name;
        return NeverTouch.Any(s => type.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A tutorial the bot may dismiss: an FTUE popup, or any popup holding an FTUE confirm button, and never one on the deny list.</summary>
    private static bool IsTutorial(Node modal) =>
        !NeverClick(modal) && (modal is NFtue || ConfirmButton(modal) is not null);

    /// <summary>
    /// An NFtueConfirmButton anywhere in the popup, or in a tutorial the button by its confirm
    /// (or, for the combat rules, next-page) node name; a shown one first. Popups are small, so
    /// walking one per poll is cheap.
    /// </summary>
    private static NClickableControl? ConfirmButton(Node modal)
    {
        if (NeverClick(modal)) return null;
        var ftue = modal is NFtue;
        NClickableControl? hidden = null;
        foreach (var button in Nodes.FindAll<NClickableControl>(modal))
        {
            var name = button.Name.ToString();
            var match = button is NFtueConfirmButton
                || (ftue && name == FtueConfirmName)
                || (modal is NCombatRulesFtue && name == RulesNextName);
            if (!match) continue;
            if (Nodes.IsShown(button)) return button;
            hidden ??= button;
        }
        return hidden;
    }

    private static void LogNoButton(Node modal)
    {
        if (_loggedNoButton) return;
        _loggedNoButton = true;
        var seen = Nodes.FindAll<NClickableControl>(modal)
            .Select(c => $"{c.GetType().Name}({c.Name})")
            .Distinct()
            .Take(12);
        ModEntry.LogError($"tutorial popup {modal.GetType().Name}: no confirm button found; buttons here: {string.Join(", ", seen)}");
    }
}

/// <summary>
/// Outside a run: the main menu, its submenus, or character select. Starting or continuing a
/// run is Mark's call, so this only reports where we are.
/// </summary>
internal sealed class MenuReader : IScreen
{
    public static readonly MenuReader Instance = new();

    public string Name => "menu";

    private MenuReader() { }

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        // The router sends everything outside a run here. If RunManager already reports the run
        // as over while its game over screen is up, read that screen rather than "the menu".
        if (node is NGameOverScreen)
            return GameOverReader.Instance.Capture(ctx, node);

        var where = node is not null && GodotObject.IsInstanceValid(node) ? node.GetType().Name : "main menu";
        var waiting = node switch
        {
            NCharacterSelectScreen => "in character select; start the run and I'll take over",
            // Also what a failed Continue leaves behind: it shows an error popup but never cleans the run up.
            _ when RunManager.Instance?.IsInProgress == true => "a run is starting; if nothing happens, start or continue a run and I'll take over",
            _ => "on the main menu; start or continue a run and I'll take over",
        };

        return new GameSnapshot
        {
            Screen = Name,
            AwaitingDecision = false,
            Waiting = waiting,
            State = new JsonObject { ["menu"] = where },
        };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId) =>
        node is NGameOverScreen
            ? GameOverReader.Instance.Execute(ctx, node, actionId)
            : ActionResult.Fail("Nothing to do on the menu; Mark starts the run.");
}

/// <summary>
/// The Crystal Sphere event's minigame (NCrystalSphereScreen), local and unsynced: an 11x11
/// fog grid hiding items, and a few divinations. "cell:x,y" spends one on a hidden cell (the
/// big tool, the default, clears the 3x3 around it; the small one just that cell), and
/// "tool:big" / "tool:small" switch tools. Once the divinations run out, the items cleared
/// completely come as a rewards screen pushed on top (RewardsReader), then "proceed" opens the
/// map, leaving this screen under it until the next room.
/// </summary>
internal sealed class CrystalSphereReader : IScreen
{
    public static readonly CrystalSphereReader Instance = new();

    public string Name => "crystal_sphere";

    /// <summary>The screen fades in for half a second before the items show.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1);

    /// <summary>Divinations closer together than this can overlap one still clearing its cells.</summary>
    private static readonly TimeSpan ClickGap = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan DivineTimeout = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan ProceedTimeout = TimeSpan.FromSeconds(10);

    private const string Legend = "grid rows run top to bottom and columns left to right, both from 0; # fog, . nothing, "
        + "R relic, P potion, C card reward, G gold, X curse (a Doubt; bad). Letters are item parts already cleared; "
        + "an item is yours once all its cells are clear";

    private CrystalSphereReader() { }

    // ---------- per-screen memory (reset when a different sphere shows up) ----------

    private static ulong _screenId;
    private static DateTime _firstSeen;
    private static bool _loggedNoGame;

    private static void Track(NCrystalSphereScreen screen)
    {
        var id = screen.GetInstanceId();
        if (id == _screenId) return;
        _screenId = id;
        _firstSeen = DateTime.UtcNow;
        _loggedNoGame = false;
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (node is not NCrystalSphereScreen screen || !Nodes.Alive(screen))
            return GameSnapshot.Busy(Name, "crystal sphere closing");

        Track(screen);
        var game = Game(screen);
        var state = game is null ? new JsonObject() : BuildState(game);
        var proceed = Nodes.Get<NProceedButton>(screen, "%ProceedButton");

        string? waiting = null;
        var actions = new List<LegalAction>();
        if (DateTime.UtcNow - _firstSeen < Settle)
            waiting = "crystal sphere appearing";
        else if (MiscUi.Usable(proceed))
        {
            if (Clicks.RecentlyClicked(proceed))
                waiting = "leaving the crystal sphere";
            else
                actions.Add(new LegalAction("proceed", "Leave the Crystal Sphere for the map"));
        }
        else if (game is null)
            waiting = "can't read the Crystal Sphere; please play it by hand";
        else if (game.DivinationCount <= 0)
            waiting = "the Crystal Sphere is handing out its rewards";
        else
        {
            // Only fog: a click on a cleared cell still spends a divination.
            foreach (var cell in Cells(screen).OrderBy(c => c.Entity.Y).ThenBy(c => c.Entity.X))
            {
                if (!cell.Entity.IsHidden || !Nodes.IsShown(cell)) continue;
                var (x, y) = (cell.Entity.X, cell.Entity.Y);
                actions.Add(new LegalAction($"cell:{x},{y}", $"Divine at column {x}, row {y}"));
            }

            var big = game.CrystalSphereTool == CrystalSphereMinigame.CrystalSphereToolType.Big;
            if (actions.Count == 0)
                waiting = "no fog left to divine";
            else if (MiscUi.Usable(ToolButton(screen, small: big)))
                actions.Add(big
                    ? new LegalAction("tool:small", "Switch to the small tool (clears one cell)")
                    : new LegalAction("tool:big", "Switch to the big tool (clears the 3x3 around a cell)"));
        }

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (node is not NCrystalSphereScreen screen || !Nodes.Alive(screen))
            return ActionResult.Fail("The Crystal Sphere is gone.");

        Track(screen);
        if (DateTime.UtcNow - _firstSeen < Settle)
            return ActionResult.Fail("The Crystal Sphere is still appearing.");

        if (actionId == "proceed")
            return Proceed(screen);

        if (Game(screen) is not { } game)
            return ActionResult.Fail("Can't read the Crystal Sphere.");

        if (actionId is "tool:big" or "tool:small")
        {
            var small = actionId == "tool:small";
            if (ToolButton(screen, small) is not { } button || !MiscUi.Usable(button))
                return ActionResult.Fail("That tool's button is not available.");
            // SetTool runs inside the button's handler, so nothing to wait for.
            return Clicks.Once(button, () => button.ForceClick(), small ? "the small tool button" : "the big tool button");
        }

        if (actionId.StartsWith("cell:", StringComparison.Ordinal) && TryParseCell(actionId.AsSpan(5), out var x, out var y))
            return Divine(screen, game, x, y);

        return ActionResult.Fail($"Unknown Crystal Sphere action '{actionId}'.");
    }

    // ---------- actions ----------

    private static ActionResult Divine(NCrystalSphereScreen screen, CrystalSphereMinigame game, int x, int y)
    {
        if (game.DivinationCount <= 0)
            return ActionResult.Fail("No divinations left.");
        if (Cells(screen).FirstOrDefault(c => c.Entity.X == x && c.Entity.Y == y) is not { } cell)
            return ActionResult.Fail($"There is no cell at column {x}, row {y}.");
        if (!cell.Entity.IsHidden)
            return ActionResult.Fail("That cell is already clear; divining it would waste a divination.");

        var before = game.DivinationCount;
        var area = Area(game, x, y);
        var click = Clicks.Once(cell, () => cell.ForceClick(), $"the cell at column {x}, row {y}");
        if (!click.Ok)
            return click;

        // Done once the divination is spent and its cells are clear (a revealed curse is added
        // to the deck before the rest of the 3x3 clears). The minimum wait spaces the clicks.
        var id = screen.GetInstanceId();
        ActionTracker.Begin(
            $"divining at column {x}, row {y}",
            _ => !Nodes.Alive(screen)
                || !Nodes.IsTopOverlay(id)
                || (game.DivinationCount < before && area.All(c => !c.IsHidden)),
            DivineTimeout,
            ClickGap);
        return ActionResult.Success;
    }

    private static ActionResult Proceed(NCrystalSphereScreen screen)
    {
        if (Nodes.Get<NProceedButton>(screen, "%ProceedButton") is not { } button || !MiscUi.Usable(button))
            return ActionResult.Fail("The proceed button is not enabled yet.");

        var click = Clicks.Once(button, () => button.ForceClick(), "the proceed button");
        if (!click.Ok)
            return click;

        // Proceed opens the map over this screen, which stays on the overlay stack.
        var id = screen.GetInstanceId();
        ActionTracker.Begin(
            "leaving the crystal sphere",
            _ => !Nodes.Alive(screen) || !Nodes.IsTopOverlay(id) || NMapScreen.Instance?.IsOpen == true,
            ProceedTimeout);
        return ActionResult.Success;
    }

    // ---------- reading ----------

    /// <summary>The minigame behind the screen, a private field: the only way to its divinations and tool.</summary>
    private static CrystalSphereMinigame? Game(NCrystalSphereScreen screen)
    {
        if (Nodes.TryGetPrivate(screen, "_entity") is CrystalSphereMinigame game)
            return game;
        if (!_loggedNoGame)
        {
            _loggedNoGame = true;
            ModEntry.LogError("crystal sphere: NCrystalSphereScreen._entity is missing; leaving the minigame to Mark");
        }
        return null;
    }

    private static List<NCrystalSphereCell> Cells(NCrystalSphereScreen screen) =>
        Nodes.FindAll<NCrystalSphereCell>(Nodes.Get<Node>(screen, "%Cells"));

    private static NDivinationButton? ToolButton(NCrystalSphereScreen screen, bool small) =>
        Nodes.Get<NDivinationButton>(screen, small ? "%SmallDivinationButton" : "%BigDivinationButton");

    /// <summary>The cells a divination at (x, y) clears with the current tool, clipped to the grid.</summary>
    private static List<CrystalSphereCell> Area(CrystalSphereMinigame game, int x, int y)
    {
        var size = game.GridSize;
        var r = game.CrystalSphereTool == CrystalSphereMinigame.CrystalSphereToolType.Big ? 1 : 0;
        var area = new List<CrystalSphereCell>();
        for (var cx = Math.Max(0, x - r); cx <= Math.Min(size.X - 1, x + r); cx++)
            for (var cy = Math.Max(0, y - r); cy <= Math.Min(size.Y - 1, y + r); cy++)
                area.Add(game.cells[cx, cy]);
        return area;
    }

    private static bool TryParseCell(ReadOnlySpan<char> text, out int x, out int y)
    {
        x = y = -1;
        var comma = text.IndexOf(',');
        return comma > 0 && int.TryParse(text[..comma], out x) && int.TryParse(text[(comma + 1)..], out y);
    }

    /// <summary>What a human sees: the fog, cleared cells, and the parts of items showing through.</summary>
    private static JsonObject BuildState(CrystalSphereMinigame game)
    {
        var size = game.GridSize;
        var rows = new JsonArray();
        for (var y = 0; y < size.Y; y++)
        {
            var row = new char[size.X];
            for (var x = 0; x < size.X; x++)
            {
                var cell = game.cells[x, y];
                row[x] = cell.IsHidden ? '#' : cell.Item is { } item ? Kind(item).Letter : '.';
            }
            rows.Add(new string(row));
        }

        var uncovered = new JsonArray();
        foreach (var item in game.Items)
            if (IsCleared(game, item))
                uncovered.Add(Kind(item).Name);

        return new JsonObject
        {
            ["divinations_left"] = game.DivinationCount,
            ["tool"] = game.CrystalSphereTool == CrystalSphereMinigame.CrystalSphereToolType.Big
                ? "big: clears the 3x3 around the chosen cell"
                : "small: clears just the chosen cell",
            ["grid"] = rows,
            ["legend"] = Legend,
            ["uncovered"] = uncovered,
        };
    }

    private static bool IsCleared(CrystalSphereMinigame game, CrystalSphereItem item)
    {
        for (var i = 0; i < item.Size.X; i++)
            for (var j = 0; j < item.Size.Y; j++)
                if (game.cells[item.Position.X + i, item.Position.Y + j].IsHidden)
                    return false;
        return true;
    }

    private static (char Letter, string Name) Kind(CrystalSphereItem item) => item switch
    {
        CrystalSphereRelic => ('R', "relic"),
        CrystalSpherePotion => ('P', "potion"),
        CrystalSphereCardReward => ('C', "card reward"),
        CrystalSphereGold => ('G', "gold"),
        CrystalSphereCurse => ('X', "curse"),
        _ => ('?', item.GetType().Name),
    };
}

/// <summary>Node checks and label reading shared by the readers in this file.</summary>
file static class MiscUi
{
    /// <summary>Clickable by a human right now: alive, drawn and enabled (ForceClick checks none of it).</summary>
    public static bool Usable([NotNullWhen(true)] NClickableControl? button) =>
        Nodes.Alive(button) && Nodes.IsShown(button) && button.IsEnabled;

    /// <summary>Every visible label under <paramref name="root"/>, in tree order, without repeats.</summary>
    public static string VisibleText(Node root)
    {
        var parts = new List<string>();
        foreach (var control in Nodes.FindAll<Godot.Control>(root))
        {
            if (control is not (Godot.Label or Godot.RichTextLabel) || !control.IsVisibleInTree()) continue;
            var text = Nodes.LabelText(control);
            if (text.Length > 0 && !parts.Contains(text)) parts.Add(text);
        }
        return string.Join(" / ", parts);
    }

    public static string Cap(string text, int max) =>
        text.Length <= max ? text : text[..(max - 3)].TrimEnd() + "...";
}
