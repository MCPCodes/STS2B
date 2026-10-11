using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using SpireMind.Game.Coop;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// A rest site (NRestSiteRoom): the campfire options (Rest, Smith, and whatever relics and
/// co-op add: Dig, Lift, Cook, Mend...) and the proceed button. Choosing is a click on the
/// option's button, which the game routes through RestSiteSynchronizer.ChooseLocalOption
/// (docs/game-api.md section e), so co-op sees our choice. Smith and Cook then open a deck
/// screen that CardSelectReader drives; backing out of it brings the options back here.
///
/// Actions: "option:&lt;n&gt;" for an available state.options[n] (n is the option's index in the
/// game's list), "proceed" once the game enables it (after an option succeeds, or when none
/// are left), and the out-of-combat potion actions
/// alongside either.
///
/// Co-op adds Mend, which heals a teammate instead of us. With one teammate it is the plain
/// "option:&lt;n&gt;"; with several, "option:&lt;n&gt;&gt;p&lt;j&gt;" for each of them. Either way
/// the label names the teammate and the real heal. The game picks the target with its
/// targeting arrow (NTargetManager); <see cref="Mend"/> clicks the option, points the arrow at
/// the teammate's seat and finishes it in one step, so the target goes out through the game's
/// own player-choice sync. Outside a co-op run Mend is listed as unsupported, as before.
/// </summary>
internal sealed class RestSiteReader : IScreen
{
    public static readonly RestSiteReader Instance = new();

    public string Name => "rest";

    /// <summary>
    /// The options fade in on arrival; give their buttons a moment before offering anything. On
    /// the first visit ever they stay hidden for 0.5 s and then the rest site FTUE opens as a
    /// modal, which ModalReader dismisses.
    /// </summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    /// <summary>Resting plays a fade and a heal animation; Smith and Cook open a deck screen.</summary>
    private static readonly TimeSpan ChooseTimeout = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromSeconds(6);

    private const string MendNote = "heals a teammate chosen with the targeting arrow, which the bot can't do yet";

    private const string CoopMendNote = "co-op only: heals one teammate by their own rest amount instead of healing you; the Mend action names the teammate it heals";

    private RestSiteReader() { }

    /// <summary>One rest site option as read just now. <see cref="N"/> is its index in the room's Options.</summary>
    private sealed record OptionView(
        int N, string Id, string Title, string Text, NRestSiteButton? Button, bool Available, bool Supported, JsonObject Json);

    // ---------- per-room memory (reset when a different rest site shows up) ----------

    private static ulong _roomId;
    private static object? _roomModel;
    private static DateTime _firstSeen;
    private static bool _left;

    /// <summary>Titles of the options we chose here, in order (Miniature Tent allows several).</summary>
    private static readonly List<string> Chosen = new();

    /// <summary>Options whose read already failed once at this rest site, so a broken one doesn't log on every poll.</summary>
    private static readonly HashSet<int> Unreadable = new();

    private static void Track(NRestSiteRoom room, object? roomModel)
    {
        // Keyed on the room model too, in case the game reuses the room node for the next campfire.
        var id = room.GetInstanceId();
        if (id == _roomId && ReferenceEquals(roomModel, _roomModel)) return;
        _roomId = id;
        _roomModel = roomModel;
        _firstSeen = DateTime.UtcNow;
        _left = false;
        Chosen.Clear();
        Unreadable.Clear();
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (Room(node) is not { } room)
            return GameSnapshot.Busy(Name, "rest site closing");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return GameSnapshot.Busy(Name, "run loading");

        Track(room, rs.CurrentRoom);
        var coop = Teammates.Active(ctx);
        var options = ReadOptions(room, coop);

        // An option we chose that is clickable again was backed out of (Smith or Cook cancelled):
        // the click disables every option button at once, and a success removes the option.
        Chosen.RemoveAll(title => options.Any(o => o.Title == title && o.Available));

        var state = BuildState(ctx, rs, me, options);

        string? waiting = null;
        var actions = new List<LegalAction>();
        if (DateTime.UtcNow - _firstSeen < Settle)
        {
            waiting = "arriving at the rest site";
        }
        else if (Teammates.RoomClosed(ctx) is { } closed)
        {
            waiting = closed;
        }
        else
        {
            foreach (var o in options)
            {
                if (!Offered(o)) continue;
                if (coop && IsMend(o.Id)) actions.AddRange(MendActions(ctx, room, o));
                else actions.Add(new LegalAction($"option:{o.N}", Label(o, me)));
            }

            if (!OptionRunning(options) && CanClick(room.ProceedButton))
                actions.Add(new LegalAction("proceed", ProceedLabel(options)));

            // Potion actions alone are not a rest site decision; they ride along with the real ones.
            if (actions.Count > 0)
                actions.AddRange(PotionActions.Legal(ctx, false));
            else
                waiting = IdleReason(options);
        }

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (Teammates.RoomClosed(ctx) is { } closed)
            return ActionResult.Fail($"Not now: {closed}.");

        if (PotionActions.TryExecute(ctx, actionId, false, out var potionResult))
            return potionResult;

        if (Room(node) is not { } room)
            return ActionResult.Fail("The rest site is gone.");
        if (ctx.Run is not { } rs || ctx.Me is null)
            return ActionResult.Fail("No run in progress.");

        Track(room, rs.CurrentRoom);

        if (actionId == "proceed")
            return Proceed(rs, room);

        var coop = Teammates.Active(ctx);
        if (coop && MendId(actionId) is var (mendOption, mendSlot))
            return Mend(ctx, rs, room, mendOption, mendSlot);

        if (actionId.StartsWith("option:", StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(7), out var n))
        {
            // Co-op: the plain id is Mend too while only one teammate can be mended.
            if (coop && ReadOptions(room, coop: true).FirstOrDefault(o => o.N == n) is { } chosen && IsMend(chosen.Id))
                return Mend(ctx, rs, room, n, slot: null);
            return Choose(rs, room, n);
        }

        return ActionResult.Fail($"Unknown rest site action '{actionId}'.");
    }

    // ---------- acting ----------

    private static ActionResult Choose(RunState rs, NRestSiteRoom room, int n)
    {
        var option = ReadOptions(room).FirstOrDefault(o => o.N == n);
        if (option is null)
            return ActionResult.Fail($"There is no rest site option {n}.");
        if (!option.Supported)
            return ActionResult.Fail($"{option.Title} {MendNote}.");
        if (!option.Available || option.Button is not { } button)
            return ActionResult.Fail($"{option.Title} is not available right now.");

        var overlayBefore = Nodes.TopOverlay();
        var roomModel = rs.CurrentRoom;
        var proceedBefore = CanClick(room.ProceedButton);

        var click = Clicks.Once(button, () =>
        {
            Chosen.Remove(option.Title);
            Chosen.Add(option.Title);
            button.ForceClick();
        }, $"the {option.Title} option");
        if (!click.Ok)
            return click;

        // The click disables every option button at once (DisableOptions), and Smith and Cook
        // push their deck screen inside it, so this mostly catches a click the button ignored.
        // After that OptionRunning keeps proceed back until the option has played out.
        ActionTracker.Begin(
            $"choosing {option.Title}",
            c => !Nodes.Alive(room)
                || !Nodes.Alive(button) || !Nodes.IsShown(button) || !button.IsEnabled
                || !ReferenceEquals(Nodes.TopOverlay(), overlayBefore)
                || Nodes.ModalOpen()
                || !ReferenceEquals(c.Run?.CurrentRoom, roomModel)
                || (!proceedBefore && CanClick(room.ProceedButton)),
            ChooseTimeout);
        return ActionResult.Success;
    }

    // ---------- co-op: Mend ----------

    /// <summary>"option:&lt;n&gt;&gt;p&lt;j&gt;" as (n, j), or null for any other id.</summary>
    private static (int Option, int Slot)? MendId(string actionId)
    {
        if (!actionId.StartsWith("option:", StringComparison.Ordinal)) return null;
        var rest = actionId.AsSpan(7);
        var arrow = rest.IndexOf(">p", StringComparison.Ordinal);
        if (arrow <= 0) return null;
        return int.TryParse(rest[..arrow], out var n) && int.TryParse(rest[(arrow + 2)..], out var slot) ? (n, slot) : null;
    }

    /// <summary>Teammates Mend can be aimed at: alive, and seated at this fire (the arrow needs their figure).</summary>
    private static List<Teammates.Mate> MendTargets(GameContext ctx, NRestSiteRoom room) =>
        Teammates.Safe("the Mend targets", new List<Teammates.Mate>(), () =>
            Teammates.Others(ctx)
                .Where(m => m.Player.Creature.IsAlive && room.GetCharacterForPlayer(m.Player) is { } seat && Nodes.Alive(seat))
                .ToList());

    /// <summary>
    /// Mend as actions, with what it really does: the teammate's own rest heal, nothing for us.
    /// One teammate: the option's plain id, like any other option. Several: one id per teammate.
    /// </summary>
    private static IEnumerable<LegalAction> MendActions(GameContext ctx, NRestSiteRoom room, OptionView o)
    {
        var targets = MendTargets(ctx, room);
        foreach (var m in targets)
        {
            var c = m.Player.Creature;
            yield return new LegalAction(
                targets.Count == 1 ? $"option:{o.N}" : $"option:{o.N}>{m.Id}",
                $"{o.Title} {m.Label}: heals them {MendHeal(m.Player)} HP ({c.CurrentHp}/{c.MaxHp} now), uses up your rest site choice and heals you nothing");
        }
    }

    /// <summary>What Mend restores to a player: their own rest amount (30% of their max HP after their relics), capped at what they are missing.</summary>
    private static int MendHeal(Player target)
    {
        try
        {
            var c = target.Creature;
            return Math.Max(0, Math.Min((int)MendRestSiteOption.GetHealAmount(target), c.MaxHp - c.CurrentHp));
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Mend a teammate. The click starts the game's targeting arrow synchronously; pointing it
    /// and finishing it happen in this same main-thread step, so nothing (a stray mouse move
    /// over the window cancels the arrow) can get in between. The target then leaves through
    /// the game's own player-choice sync, exactly as when a person aims: safe on a client and
    /// on the host alike. Whatever goes wrong after the click cancels the arrow, which the
    /// game treats as "no target": the option is not used up and the buttons come back.
    /// </summary>
    /// <param name="slot">The teammate's player slot, or null for the only teammate who can be mended.</param>
    private static ActionResult Mend(GameContext ctx, RunState rs, NRestSiteRoom room, int n, int? slot)
    {
        var option = ReadOptions(room, coop: true).FirstOrDefault(o => o.N == n);
        if (option is null || !IsMend(option.Id))
            return ActionResult.Fail($"Rest site option {n} is not Mend.");
        // IsEnabled is off on every option button while one runs; a second click would send the choice twice.
        if (!option.Available || option.Button is not { } button)
            return ActionResult.Fail($"{option.Title} is not available right now.");

        var targets = MendTargets(ctx, room);
        if (slot is null && targets.Count != 1)
            return ActionResult.Fail(targets.Count == 0
                ? $"Nobody can be mended right now."
                : $"{option.Title} needs a teammate: {string.Join(", ", targets.Select(m => $"option:{n}>{m.Id}"))}.");
        var target = slot is { } wanted ? targets.FirstOrDefault(m => m.Slot == wanted) : targets[0];
        if (target is null || room.GetCharacterForPlayer(target.Player) is not { } seat)
            return ActionResult.Fail($"p{slot} can't be mended right now.");

        // The arrow cancels itself every frame while an overlay or a capstone screen is open.
        if (NOverlayStack.Instance?.ScreenCount > 0 || NCapstoneContainer.Instance?.InUse == true || Nodes.ModalOpen())
            return ActionResult.Fail("Another screen is open over the rest site.");
        if (NRun.Instance?.GlobalUi?.TargetManager is not { } aim || !Nodes.Alive(aim))
            return ActionResult.Fail("The game's targeting arrow is not available.");
        if (aim.IsInSelection)
            return ActionResult.Fail("A targeting arrow is already up.");

        var creature = target.Player.Creature;
        var hpBefore = creature.CurrentHp;
        var roomModel = rs.CurrentRoom;
        var proceedBefore = CanClick(room.ProceedButton);

        string? failure = null;
        var click = Clicks.Once(button, () =>
        {
            Chosen.Remove(option.Title);
            Chosen.Add(option.Title);
            button.ForceClick();
            try
            {
                failure = Aim(aim, seat);
            }
            catch (Exception ex)
            {
                // Never leave the arrow up: the option would hang on it, and the group on us.
                failure = $"Aiming Mend failed ({ex.GetType().Name}: {ex.Message}).";
                try { aim.CancelTargeting(); }
                catch (Exception cancel) { ModEntry.LogError($"coop: Mend: cancelling the arrow failed too: {cancel.Message}"); }
            }
        }, $"the {option.Title} option");
        if (!click.Ok)
            return click;
        if (failure is not null)
        {
            Chosen.Remove(option.Title);
            ModEntry.Log($"coop: Mend on {target.Label} failed: {failure}");
            return ActionResult.Fail(failure);
        }

        ModEntry.Log($"coop: Mend aimed at {target.Label} ({hpBefore}/{creature.MaxHp} hp, heals {MendHeal(target.Player)})");
        ActionTracker.Begin(
            $"mending {target.Name}",
            c => !Nodes.Alive(room)
                || creature.CurrentHp != hpBefore
                || Nodes.ModalOpen()
                || !ReferenceEquals(c.Run?.CurrentRoom, roomModel)
                || (!proceedBefore && CanClick(room.ProceedButton)),
            ChooseTimeout);
        return ActionResult.Success;
    }

    /// <summary>
    /// Points the arrow the Mend click just started at a teammate's seat and finishes it; null
    /// when it went through. A left mouse release is how mouse targeting ends, and the
    /// manager's own input handler is public; with a controller in use that handler ignores the
    /// mouse, so the manager's FinishTargeting (private in C#, but in the node's Godot method
    /// table) is the fallback. Neither was seen live yet: the log says which one ran.
    /// </summary>
    private static string? Aim(NTargetManager aim, NRestSiteCharacter seat)
    {
        if (!aim.IsInSelection)
            return "The game ignored the Mend click.";

        if (!aim.AllowedToTargetNode(seat))
        {
            aim.CancelTargeting();
            return "The game does not allow that Mend target.";
        }

        aim.OnNodeHovered(seat);
        using (var release = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false })
            aim._Input(release);
        if (!aim.IsInSelection)
        {
            ModEntry.Log("coop: Mend: the arrow was finished with a mouse release");
            return null;
        }

        ModEntry.Log("coop: Mend: the mouse release did not end targeting; calling FinishTargeting");
        aim.Call(NTargetManager.MethodName.FinishTargeting, false);
        if (aim.IsInSelection)
        {
            aim.CancelTargeting();
            return "The Mend arrow could not be finished and was cancelled.";
        }
        return null;
    }

    private static ActionResult Proceed(RunState rs, NRestSiteRoom room)
    {
        var proceed = room.ProceedButton;
        if (proceed is null || !Nodes.IsShown(proceed))
            return ActionResult.Fail("There is no proceed button.");
        // A disabled proceed still opens the map when force-clicked, but with travel left off.
        if (!proceed.IsEnabled)
            return ActionResult.Fail("The proceed button is not enabled yet.");
        if (OptionRunning(ReadOptions(room)))
            return ActionResult.Fail("A rest site option is still playing out.");

        var roomModel = rs.CurrentRoom;
        var click = Clicks.Once(proceed, () =>
        {
            _left = true;
            proceed.ForceClick();
        }, "the proceed button");
        if (!click.Ok)
            return click;

        ActionTracker.Begin(
            "leaving the rest site",
            c => !Nodes.Alive(room) || NMapScreen.Instance?.IsOpen == true || Nodes.ModalOpen() || !ReferenceEquals(c.Run?.CurrentRoom, roomModel),
            LeaveTimeout);
        return ActionResult.Success;
    }

    // ---------- reading ----------

    // v0.107.1 syncs nothing at the rest site beyond each choice: no waiting on teammates
    // before proceed or on leaving (only the map vote waits for everyone).
    private static string IdleReason(List<OptionView> options)
    {
        if (_left)
            return "leaving the rest site";

        if (options.Any(o => o.Available && !o.Supported))
            return "only Mend is available, which the bot can't target; a human has to choose it";

        if (Chosen.Count > 0)
            return $"{Chosen[^1]} in progress";

        return options.Count == 0 ? "rest site options appearing" : "no rest site option is clickable yet";
    }

    private static List<OptionView> ReadOptions(NRestSiteRoom room, bool coop = false)
    {
        var views = new List<OptionView>();
        if (room.Options is not { } options) return views;

        // n stays the option's index in the game's list (what ChooseLocalOption gets), even
        // when an option in between can't be read.
        var n = 0;
        foreach (var option in options)
        {
            var i = n++;
            try
            {
                views.Add(View(room, option, i, coop));
            }
            catch (Exception ex)
            {
                if (Unreadable.Add(i)) ModEntry.LogError($"reading rest site option {i}: {ex.Message}");
            }
        }
        return views;
    }

    private static OptionView View(NRestSiteRoom room, RestSiteOption option, int n, bool coop)
    {
        // HEAL, SMITH, MEND (co-op), or a relic's COOK, DIG, LIFT, KINDLE, CLONE, HATCH.
        var id = option.OptionId;
        var title = Describe.Text(option.Title);
        if (title.Length == 0) title = Capitalize(id.ToLowerInvariant());
        var text = Describe.SafeText(() => option.Description);

        // Null while the room rebuilds its buttons after a choice; the old ones are queued for deletion.
        var button = room.GetButtonForOption(option);
        if (button is not null && !Nodes.Alive(button)) button = null;

        // The button ignores a click when its option was disabled at creation (no card to upgrade,
        // too few to cook), and IsEnabled is off on every button while any option runs.
        var available = option.IsEnabled && button is not null && Nodes.IsShown(button) && button.IsEnabled;
        // Mend is only driven in a real co-op run; anywhere else it stays listed as unsupported.
        var supported = !IsMend(id) || coop;

        var json = new JsonObject { ["n"] = n, ["id"] = id, ["name"] = title };
        if (text.Length > 0) json["text"] = text;
        if (!available) json["available"] = false;
        if (!supported)
        {
            json["supported"] = false;
            json["note"] = MendNote;
        }
        else if (IsMend(id))
        {
            json["note"] = CoopMendNote;
        }
        return new OptionView(n, id, title, text, button, available, supported, json);
    }

    private static bool Offered(OptionView o) =>
        o.Available && o.Supported && o.Button is { } b && !Clicks.RecentlyClicked(b);

    /// <summary>
    /// An option is playing out: the room disables every option button for it (DisableOptions,
    /// then the hide tween) and only re-enables or rebuilds them once it is over. With Miniature
    /// Tent proceed is already on by then, and leaving now would cut the option short.
    /// </summary>
    private static bool OptionRunning(List<OptionView> options) =>
        options.Any(o => o.Button is { IsEnabled: false });

    private static JsonObject BuildState(GameContext ctx, RunState rs, Player me, List<OptionView> options)
    {
        var player = Describe.PlayerRun(me, includeDeck: true);
        player.Remove("potions"); // replaced by the detailed slots below
        player.Remove("hp");      // top level below
        player.Remove("max_hp");

        var state = new JsonObject
        {
            ["run"] = Describe.RunInfo(rs),
            ["hp"] = me.Creature.CurrentHp,
            ["max_hp"] = me.Creature.MaxHp,
        };

        // What the next boss is decides between resting and which card to upgrade.
        if (rs.Act is { } act)
        {
            var boss = Describe.SafeText(() => act.BossEncounter?.Title);
            if (boss.Length > 0) state["boss"] = boss;
            var second = Describe.SafeText(() => act.SecondBossEncounter?.Title);
            if (second.Length > 0) state["second_boss"] = second;
        }

        state["player"] = player;
        state["potions"] = PotionActions.Describe(ctx, false);

        // Teammates' HP matters for Mend and for how safe the next fights are.
        if (Teammates.Team(ctx) is { } coopTeam)
        {
            state["team"] = coopTeam;
            state["co_op"] = Teammates.RoomNote(ctx, CoopManners.RestNote);
        }
        else if (ctx.IsMultiplayer && Describe.TeamHp(ctx.Teammates) is { Count: > 0 } team)
            state["team"] = team;

        var list = new JsonArray();
        foreach (var o in options) list.Add(o.Json);
        state["options"] = list;

        if (Chosen.Count > 0)
        {
            var chosen = new JsonArray();
            foreach (var title in Chosen) chosen.Add(title);
            state["chosen"] = chosen;
        }
        return state;
    }

    private static string Label(OptionView o, Player me)
    {
        if (o.Id == "HEAL") return $"{o.Title} ({HealHint(me)})";
        if (o.Id == "SMITH") return $"{o.Title} (upgrade a card)";
        return o.Text.Length > 0 ? $"{o.Title} ({Short(o.Text)})" : o.Title;
    }

    /// <summary>"heal 24": 30% of max HP after relics, the number the option's text shows (HP rounds down).</summary>
    private static string HealHint(Player me)
    {
        try { return $"heal {(int)HealRestSiteOption.GetHealAmount(me)}"; }
        catch { return "heal"; }
    }

    // Proceed only turns on after an option succeeded or when none are left, so options still
    // on offer beside it are extras a relic allows (Miniature Tent).
    private static string ProceedLabel(List<OptionView> options) =>
        options.Any(o => o.Available && o.Supported) ? "Proceed (leave the remaining options unused)" : "Proceed";

    // ---------- helpers ----------

    private static bool IsMend(string id) => id == "MEND";

    private static NRestSiteRoom? Room(Node? node)
    {
        var room = node as NRestSiteRoom ?? NRestSiteRoom.Instance;
        return room is not null && Nodes.Alive(room) ? room : null;
    }

    private static bool CanClick([NotNullWhen(true)] NProceedButton? button) =>
        button is not null && Nodes.Alive(button) && Nodes.IsShown(button) && button.IsEnabled && !Clicks.RecentlyClicked(button);

    /// <summary>The first sentence, kept short enough for an action label.</summary>
    private static string Short(string text)
    {
        var end = text.IndexOf(". ", StringComparison.Ordinal);
        var s = (end > 0 ? text[..end] : text).TrimEnd('.', ' ');
        return s.Length <= 70 ? s : s[..67].TrimEnd() + "...";
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
