using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// The end of a run (NGameOverScreen on the overlay stack): win, loss or abandon, where it
/// ended, and the final build, so the bot can write its post-mortem before leaving. The only
/// action is "continue", one click on the screen's continue or return-to-menu button once it
/// is shown and enabled; it keeps the same id whichever of the two is up.
/// </summary>
internal sealed class GameOverReader : IScreen
{
    public static readonly GameOverReader Instance = new();

    public string Name => "game_over";

    /// <summary>The screen fades in over the last room; don't offer a click before it has.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(800);

    /// <summary>How often Capture may walk the screen's tree while the button or score is missing.</summary>
    private static readonly TimeSpan Rescan = TimeSpan.FromMilliseconds(500);

    /// <summary>Stop looking for a score label after this long; it isn't on this screen.</summary>
    private static readonly TimeSpan ScoreSearch = TimeSpan.FromSeconds(10);

    /// <summary>No continue button after this long gets logged once, with the buttons that are there.</summary>
    private static readonly TimeSpan NoButtonLog = TimeSpan.FromSeconds(8);

    /// <summary>Continuing can run the run's clean-up and a transition before the menu shows.</summary>
    private static readonly TimeSpan ContinueTimeout = TimeSpan.FromSeconds(10);

    private const int MaxScoreChars = 120;

    /// <summary>
    /// Matched by class name, then by node name, so a rename hides the action instead of breaking the build.
    /// VERIFY: the game over screen's buttons are classes NGameOverContinueButton / NReturnToMainMenuButton deriving from NClickableControl
    /// </summary>
    private static readonly string[] ContinueButtonTypes = ["NGameOverContinueButton", "NReturnToMainMenuButton"];

    // VERIFY: node names of the game over screen's continue / main menu buttons contain one of these (fallback when the classes differ)
    private static readonly string[] ContinueButtonNames = ["Continue", "MainMenu"];

    private GameOverReader() { }

    // ---------- per-screen memory (reset when a different game over screen shows up) ----------

    private static ulong _screenId;
    private static DateTime _firstSeen;
    private static DateTime _lastScan;
    private static NClickableControl? _button;
    private static List<Godot.Control> _scoreLabels = new();
    private static bool _loggedNoButton;

    private static void Track(NGameOverScreen screen)
    {
        var id = screen.GetInstanceId();
        if (id == _screenId) return;
        _screenId = id;
        _firstSeen = DateTime.UtcNow;
        _lastScan = DateTime.MinValue;
        _button = null;
        _scoreLabels = new List<Godot.Control>();
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
            Actions = [new LegalAction("continue", "Continue to the main menu")],
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
        }

        var score = ScoreText();
        if (score.Length > 0) state["score"] = score;

        if (ctx.Me is { } me)
            state["player"] = Describe.PlayerRun(me, includeDeck: true);
        return state;
    }

    /// <summary>
    /// Finds the continue button and the score labels with one walk of the screen, at most every
    /// <see cref="Rescan"/>, and only while one of them is missing or the button was hidden (the
    /// screen can swap its continue button for a main menu one). <paramref name="force"/> walks now.
    /// </summary>
    private static void Refresh(NGameOverScreen screen, DateTime now, bool force)
    {
        var haveButton = _button is { } b && Nodes.Alive(b) && Nodes.IsShown(b);
        var doneWithScore = _scoreLabels.Count > 0 || now - _firstSeen > ScoreSearch;
        if (!force && haveButton && doneWithScore) return;
        if (!force && now - _lastScan < Rescan) return;
        _lastScan = now;

        NClickableControl? shown = null;
        NClickableControl? hidden = null;
        var labels = new List<Godot.Control>();
        foreach (var child in Nodes.FindAll<Node>(screen))
        {
            if (child is NClickableControl button)
            {
                if (!IsContinueButton(button)) continue;
                if (Nodes.IsShown(button)) shown ??= button;
                else hidden ??= button;
            }
            // VERIFY: the game over screen's score label has "Score" in its node name (Label or RichTextLabel, e.g. MegaLabel)
            else if (child is Godot.Control label && label is Godot.Label or Godot.RichTextLabel
                     && label.Name.ToString().Contains("Score", StringComparison.OrdinalIgnoreCase))
            {
                labels.Add(label);
            }
        }

        _button = shown ?? hidden;
        if (labels.Count > 0) _scoreLabels = labels;
    }

    private static bool IsContinueButton(NClickableControl button)
    {
        var type = button.GetType().Name;
        if (ContinueButtonTypes.Contains(type, StringComparer.Ordinal)) return true;
        var name = button.Name.ToString();
        return ContinueButtonNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private static string ScoreText()
    {
        var parts = new List<string>();
        foreach (var label in _scoreLabels)
        {
            if (!Nodes.Alive(label) || !label.IsVisibleInTree()) continue;
            var text = Nodes.LabelText(label);
            if (text.Length > 0 && !parts.Contains(text)) parts.Add(text);
        }
        return MiscUi.Cap(string.Join(" / ", parts), MaxScoreChars);
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
/// get "dismiss", one click on their confirm button. Everything else (error popups, the
/// mod-loading consent prompt whose Yes saves and quits the game, the abandon-run
/// confirmation, anything unknown) is left for Mark: no actions, and a wait that names the
/// popup's class.
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

    // VERIFY: the tutorial popups' classes live in MegaCrit.Sts2.Core.Nodes.Ftue (or have "Ftue" in their class name)
    private const string FtueNamespace = "MegaCrit.Sts2.Core.Nodes.Ftue";

    /// <summary>
    /// Matched by class name so a rename hides the action instead of breaking the build.
    /// VERIFY: the tutorial popups' confirm button class is NFtueConfirmButton, deriving from NClickableControl
    /// </summary>
    private const string FtueConfirmType = "NFtueConfirmButton";

    /// <summary>
    /// Never clicked, whatever buttons they hold: matched against the popup's class name.
    /// "ModLoading" is NConfirmModLoadingPopup, whose Yes saves and quits the game.
    /// </summary>
    private static readonly string[] NeverTouch = ["ModLoading", "ModConsent", "Error", "Abandon", "Quit", "Disconnect"];

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

    private static bool IsFtuePopup(Node modal)
    {
        var type = modal.GetType();
        return type.Namespace?.StartsWith(FtueNamespace, StringComparison.Ordinal) == true
            || type.Name.Contains("Ftue", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A tutorial the bot may dismiss: an FTUE popup, or any popup holding an FTUE confirm button, and never one on the deny list.</summary>
    private static bool IsTutorial(Node modal) =>
        !NeverClick(modal) && (IsFtuePopup(modal) || ConfirmButton(modal) is not null);

    /// <summary>
    /// An NFtueConfirmButton anywhere in the popup, or an NConfirmButton when the popup itself is
    /// a tutorial; a shown one first. Popups are small, so walking one per poll is cheap.
    /// </summary>
    private static NClickableControl? ConfirmButton(Node modal)
    {
        if (NeverClick(modal)) return null;
        var ftue = IsFtuePopup(modal);
        NClickableControl? hidden = null;
        foreach (var button in Nodes.FindAll<NClickableControl>(modal))
        {
            var match = button.GetType().Name == FtueConfirmType
                || (ftue && button is NConfirmButton); // VERIFY: NConfirmButton derives from NClickableControl
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
            _ when RunManager.Instance?.IsInProgress == true => "a run is starting",
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
/// The Crystal Sphere event's minigame (NCrystalSphereScreen). Version 1 doesn't play it:
/// it is reported as a wait for Mark, and the rewards screen that follows is read as usual.
/// </summary>
internal sealed class CrystalSphereReader : IScreen
{
    public static readonly CrystalSphereReader Instance = new();

    public string Name => "crystal_sphere";

    private CrystalSphereReader() { }

    public GameSnapshot Capture(GameContext ctx, Node? node) => new()
    {
        Screen = Name,
        AwaitingDecision = false,
        Waiting = "Crystal Sphere minigame: please play it by hand",
        State = new JsonObject { ["minigame"] = "crystal_sphere" },
    };

    public ActionResult Execute(GameContext ctx, Node? node, string actionId) =>
        ActionResult.Fail("The Crystal Sphere minigame is played by hand for now.");
}

/// <summary>Node checks and label reading shared by the readers in this file.</summary>
file static class MiscUi
{
    /// <summary>Clickable by a human right now: alive, drawn and enabled (ForceClick checks none of it).</summary>
    public static bool Usable([NotNullWhen(true)] NClickableControl? button) =>
        Nodes.Alive(button) && Nodes.IsShown(button) && button.IsEnabled; // VERIFY: NClickableControl has bool IsEnabled and ForceClick() (docs name only btn.ForceClick/IsEnabled generically)

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
