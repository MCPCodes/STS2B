using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// A treasure room (NTreasureRoom, a room rather than an overlay): open the chest, take a
/// relic, leave. The chest is the room's "Chest" node, clicked once; its relics sit in
/// NTreasureRoomRelicHolders, and clicking one calls TreasureRoomRelicSynchronizer.PickRelicLocally,
/// which is a vote in co-op (docs/game-api.md section e). Leaving before the game's
/// RelicPickingFinished() Task completes loses the relic, so "proceed" waits for that Task,
/// which is polled and never awaited. A rewards screen the chest pushes is routed to
/// RewardsReader before this reader is asked.
///
/// Actions: "open", "relic:&lt;n&gt;" for state.relics[n] while we haven't picked, "proceed",
/// and the out-of-combat potion actions alongside any of them. The relics are listed only once
/// the chest is open, as a human sees them.
/// </summary>
internal sealed class TreasureReader : IScreen
{
    public static readonly TreasureReader Instance = new();

    public string Name => "treasure";

    /// <summary>The room fades in on arrival; give the chest a moment before offering it.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    /// <summary>The relics rise out of the chest; a pick before voting opens throws in the game.</summary>
    private static readonly TimeSpan RelicSettle = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PickTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromSeconds(6);

    /// <summary>Give a co-op pick time to reach the synchronizer before reporting "waiting for teammates".</summary>
    private static readonly TimeSpan VoteSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// A chest still clickable this long after our one click didn't take it. The chest is never
    /// clicked twice (a second press can throw or double-open), so a human has to step in.
    /// </summary>
    private static readonly TimeSpan ChestStuckAfter = TimeSpan.FromSeconds(8);

    /// <summary>An opened chest that shows no relic for this long, with the room itself on screen, was empty.</summary>
    private static readonly TimeSpan EmptyChestWait = TimeSpan.FromSeconds(6);

    /// <summary>A chest we never clicked that can't be clicked and shows no relic for this long was looted before we got here (a reload).</summary>
    private static readonly TimeSpan LootedRoomWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Polls of this room closer together than this mean the room itself stayed on screen. A
    /// longer gap usually means a rewards screen was on top, and the empty-chest clocks restart.
    /// </summary>
    private static readonly TimeSpan OnScreenGap = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Singleplayer only: a pick that has neither finished the picking nor added a relic after
    /// this long didn't register, and the relics are offered again.
    /// </summary>
    private static readonly TimeSpan PickStall = TimeSpan.FromSeconds(15);

    /// <summary>Singleplayer only, when RelicPickingFinished() can't be used: how long after the pick, with no relic left pickable, leaving counts as safe.</summary>
    private static readonly TimeSpan FallbackAwardWait = TimeSpan.FromSeconds(12);

    private const string PickingFinishedMethod = "RelicPickingFinished";

    /// <summary>
    /// Members tried, in order, for a holder's relic: a RelicModel, or a relic node whose Model is one.
    /// VERIFY: how NTreasureRoomRelicHolder exposes its relic (the member name, and whether it holds the RelicModel or an NRelic node with .Model).
    /// </summary>
    private static readonly string[] HolderRelicMembers = ["Relic", "RelicModel", "Model", "_relic", "_relicModel"];

    private TreasureReader() { }

    /// <summary>One relic on offer as read just now; <see cref="N"/> is its place among the visible holders.</summary>
    private sealed record RelicView(int N, NTreasureRoomRelicHolder Holder, string Name, bool Enabled, JsonObject Json);

    // ---------- per-room memory (reset when a different treasure room shows up) ----------

    private static ulong _roomId;
    private static object? _roomModel;
    private static DateTime _firstSeen;
    private static DateTime _lastCaptured;
    private static DateTime _onScreenSince;
    private static Node? _chest;
    private static int _chestClicks;
    private static DateTime _chestClickedAt;
    private static DateTime? _relicsSince;
    private static string? _picked;
    private static DateTime _pickedAt;
    private static int _relicCountAtPick;
    private static bool _left;

    /// <summary>The game's RelicPickingFinished() Task for this room, looked up once the relics show. Never awaited.</summary>
    private static Task? _pickingDone;
    private static bool _pickingLookedUp;
    private static bool _loggedPickingFault;
    private static bool _loggedNoChest;
    private static bool _loggedUnreadableRelic;

    private static void Track(NTreasureRoom room, object? roomModel)
    {
        // Keyed on the room model too, in case the game reuses the room node for the next chest.
        var id = room.GetInstanceId();
        if (id == _roomId && ReferenceEquals(roomModel, _roomModel)) return;
        _roomId = id;
        _roomModel = roomModel;
        _firstSeen = DateTime.UtcNow;
        _lastCaptured = _firstSeen;
        _onScreenSince = _firstSeen;
        _chest = null;
        _chestClicks = 0;
        _chestClickedAt = DateTime.MinValue;
        _relicsSince = null;
        _picked = null;
        _pickedAt = DateTime.MinValue;
        _relicCountAtPick = 0;
        _left = false;
        _pickingDone = null;
        _pickingLookedUp = false;
        _loggedPickingFault = false;
        _loggedNoChest = false;
        _loggedUnreadableRelic = false;
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (Room(node) is not { } room)
            return GameSnapshot.Busy(Name, "treasure room closing");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return GameSnapshot.Busy(Name, "run loading");

        Track(room, rs.CurrentRoom);
        var now = DateTime.UtcNow;
        if (now - _lastCaptured > OnScreenGap) _onScreenSince = now;
        _lastCaptured = now;

        var chest = Chest(room);
        var relics = ReadRelics(room, chest);
        Observe(room, relics);
        CheckPickStall(ctx, me, relics, now);

        var chestOpen = ChestOpen(chest, relics);
        var state = BuildState(ctx, rs, me, relics, chestOpen);

        string? waiting = null;
        var actions = new List<LegalAction>();
        if (now - _firstSeen < Settle)
        {
            waiting = "entering the treasure room";
        }
        else
        {
            if (CanOpen(chest, chestOpen))
                actions.Add(new LegalAction("open", "Open the chest"));

            if (CanPick(now))
            {
                foreach (var r in relics)
                    if (r.Enabled && !Clicks.RecentlyClicked(r.Holder))
                        actions.Add(new LegalAction($"relic:{r.N}", ctx.IsMultiplayer ? $"Pick relic {r.Name} (co-op vote)" : $"Take relic {r.Name}"));
            }

            if (CanClick(room.ProceedButton) && ProceedSafe(ctx, me, chest, relics, now)) // VERIFY: NTreasureRoom exposes ProceedButton (an NProceedButton), like the merchant and rest site rooms
                actions.Add(new LegalAction("proceed", _picked is null && relics.Count == 0 ? "Leave the treasure room (no relic on offer)" : "Leave the treasure room"));

            // Potion actions alone are not a treasure room decision; they ride along with the real ones.
            if (actions.Count > 0)
                actions.AddRange(PotionActions.Legal(ctx, false));
            else
                waiting = IdleReason(ctx, chest, chestOpen, relics, now);
        }

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (PotionActions.TryExecute(ctx, actionId, false, out var potionResult))
            return potionResult;

        if (Room(node) is not { } room)
            return ActionResult.Fail("The treasure room is gone.");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return ActionResult.Fail("No run in progress.");

        Track(room, rs.CurrentRoom);
        var chest = Chest(room);
        var relics = ReadRelics(room, chest);
        Observe(room, relics);

        if (actionId == "open")
            return Open(room, chest);

        if (actionId == "proceed")
            return Proceed(ctx, rs, me, room, chest, relics);

        if (actionId.StartsWith("relic:", StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(6), out var n))
            return Pick(ctx, room, me, relics, n);

        return ActionResult.Fail($"Unknown treasure room action '{actionId}'.");
    }

    // ---------- acting ----------

    private static ActionResult Open(NTreasureRoom room, Node? found)
    {
        if (found is not { } chest)
            return ActionResult.Fail("Can't find the chest.");
        if (!CanOpen(chest, ChestOpen(chest, ReadRelics(room, chest))))
            return ActionResult.Fail(_chestClicks > 0 ? "The chest was already clicked once; waiting for it to open." : "The chest can't be opened right now.");

        var click = Clicks.Once(chest, () =>
        {
            _chestClicks++;
            _chestClickedAt = DateTime.UtcNow;
            Press(chest);
        }, "the chest");
        if (!click.Ok)
            return click;

        ActionTracker.Begin(
            "opening the chest",
            _ => !Nodes.Alive(room) || ReadRelics(room, chest).Count > 0 || Nodes.TopOverlay() is not null || Nodes.ModalOpen(),
            OpenTimeout);
        return ActionResult.Success;
    }

    private static ActionResult Pick(GameContext ctx, NTreasureRoom room, Player me, List<RelicView> relics, int n)
    {
        if (_picked is { } already)
            return ActionResult.Fail($"Already picked {already}.");
        if (!CanPick(DateTime.UtcNow))
            return ActionResult.Fail("The relics are still appearing.");
        var r = relics.FirstOrDefault(v => v.N == n);
        if (r is null)
            return ActionResult.Fail($"There is no relic {n}.");
        if (!r.Enabled)
            return ActionResult.Fail($"{r.Name} can't be picked right now.");

        var holder = r.Holder;
        var countBefore = me.Relics.Count();
        var overlayBefore = Nodes.TopOverlay();
        var multiplayer = ctx.IsMultiplayer;

        ActionResult click;
        try
        {
            click = Clicks.Once(holder, () =>
            {
                _picked = r.Name;
                _pickedAt = DateTime.UtcNow;
                _relicCountAtPick = countBefore;
                Press(holder);
            }, $"relic {r.Name}");
        }
        catch (Exception ex)
        {
            // PickRelicLocally throws when voting isn't open (still revealing, or already over).
            _picked = null;
            ModEntry.LogError($"picking {r.Name}: {ex.GetBaseException().Message}");
            return ActionResult.Fail($"The game refused the pick: {ex.GetBaseException().Message}");
        }
        if (!click.Ok)
            return click;

        // Singleplayer: wait for the award. Co-op: our vote is in once it has had a moment to
        // go out; Capture then reports the wait for teammates.
        ActionTracker.Begin(
            $"taking {r.Name}",
            c => !Nodes.Alive(room)
                || multiplayer
                || _pickingDone?.IsCompleted == true
                || (c.Me is { } m && m.Relics.Count() > countBefore)
                || !ReferenceEquals(Nodes.TopOverlay(), overlayBefore)
                || Nodes.ModalOpen(),
            PickTimeout,
            minWait: multiplayer ? VoteSettle : null);
        return ActionResult.Success;
    }

    private static ActionResult Proceed(GameContext ctx, RunState rs, Player me, NTreasureRoom room, Node? chest, List<RelicView> relics)
    {
        if (!ProceedSafe(ctx, me, chest, relics, DateTime.UtcNow))
            return ActionResult.Fail("The relic hasn't been awarded yet; leaving now would lose it.");

        var proceed = room.ProceedButton;
        if (proceed is null || !Nodes.IsShown(proceed))
            return ActionResult.Fail("There is no proceed button.");
        if (!proceed.IsEnabled)
            return ActionResult.Fail("The proceed button is not enabled yet.");

        var roomModel = rs.CurrentRoom;
        var click = Clicks.Once(proceed, () =>
        {
            _left = true;
            proceed.ForceClick();
        }, "the proceed button");
        if (!click.Ok)
            return click;

        ActionTracker.Begin(
            "leaving the treasure room",
            c => !Nodes.Alive(room) || NMapScreen.Instance?.IsOpen == true || Nodes.ModalOpen() || !ReferenceEquals(c.Run?.CurrentRoom, roomModel),
            LeaveTimeout);
        return ActionResult.Success;
    }

    // ---------- the picking Task ----------

    /// <summary>Per-room bookkeeping once relics show: when they appeared, and the game's picking Task.</summary>
    private static void Observe(NTreasureRoom room, List<RelicView> relics)
    {
        if (relics.Count > 0)
        {
            _relicsSince ??= DateTime.UtcNow;
            LookUpPickingTask(room, relics);
        }

        if (_pickingDone is { IsFaulted: true } faulted && !_loggedPickingFault)
        {
            _loggedPickingFault = true;
            ModEntry.LogError($"treasure room: relic picking failed: {faulted.Exception?.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// Fetch the game's RelicPickingFinished() Task once per room. Looked up by name on the
    /// holders' ancestors, then the room, then the synchronizer, so we don't depend on which
    /// of them declares it.
    /// </summary>
    private static void LookUpPickingTask(NTreasureRoom room, List<RelicView> relics)
    {
        if (_pickingLookedUp) return;
        _pickingLookedUp = true;

        // VERIFY: which object declares RelicPickingFinished() (expected the relic collection node that parents the holders), that calling it once the relics show is harmless, and that its Task completes only after our relic is awarded
        var owners = new List<object>();
        for (Node? n = relics[0].Holder.GetParent(); n is not null && !ReferenceEquals(n, room); n = n.GetParent())
            owners.Add(n);
        owners.Add(room);
        if (RunManager.Instance?.TreasureRoomRelicSynchronizer is { } sync)
            owners.Add(sync);

        // Public only: it is meant to be awaited by callers, and a private handler of the same name must not be invoked.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (var owner in owners)
        {
            var method = owner.GetType().GetMethod(PickingFinishedMethod, flags, null, Type.EmptyTypes, null);
            if (method is null || !typeof(Task).IsAssignableFrom(method.ReturnType)) continue;
            Task? task = null;
            try
            {
                task = method.Invoke(owner, null) as Task;
            }
            catch (Exception ex)
            {
                ModEntry.LogError($"treasure room: {owner.GetType().Name}.{PickingFinishedMethod}() threw: {ex.GetBaseException().Message}");
            }
            if (task is null) continue;

            // This runs when the relics first show, before we can pick. A Task that is already
            // done belongs to an earlier picking (or doesn't track this one); trusting it would
            // offer "proceed" the moment we pick, before the relic is ours.
            if (task.IsCompleted)
            {
                ModEntry.LogError($"treasure room: {owner.GetType().Name}.{PickingFinishedMethod}() was already complete before our pick; ignoring it");
                continue;
            }
            _pickingDone = task;
            return;
        }
        ModEntry.LogError($"treasure room: no usable {PickingFinishedMethod}() found; watching our relics instead");
    }

    /// <summary>
    /// True when leaving can't cost us the relic: the game says picking is finished; or, if its
    /// Task can't be had, our relic count went up; or no relic was ever offered (empty chest).
    /// </summary>
    private static bool ProceedSafe(GameContext ctx, Player me, Node? chest, List<RelicView> relics, DateTime now)
    {
        // Relics still on offer that we never picked: whatever the Task says, leaving forfeits one.
        if (_picked is null && relics.Count > 0) return false;

        if (_pickingDone is { } task)
            return task.IsCompleted;

        if (_relicsSince is not null)
        {
            if (_picked is null) return false;
            if (me.Relics.Count() > _relicCountAtPick) return true;
            // Relics still pickable this long after our pick mean it didn't register (CheckPickStall
            // offers them again); only a finished-looking display counts as done.
            return !ctx.IsMultiplayer && now - _pickedAt > FallbackAwardWait && !relics.Any(r => r.Enabled);
        }

        // Nothing was ever offered: an empty chest we opened a while ago, or one looted before we
        // arrived. A missing chest node proves neither, and a chest that is still clickable (our
        // click was ignored) still holds its relics, so neither counts. The clock only runs while
        // the room itself is on screen, so relics still rising after a rewards screen closes
        // aren't mistaken for none.
        if (chest is null || ChestClickable(chest)) return false;
        var since = _chestClicks > 0 ? _chestClickedAt : _firstSeen;
        if (_onScreenSince > since) since = _onScreenSince;
        return now - since > (_chestClicks > 0 ? EmptyChestWait : LootedRoomWait);
    }

    /// <summary>Singleplayer: a pick that did nothing for a long time is forgotten so the relics are offered again.</summary>
    private static void CheckPickStall(GameContext ctx, Player me, List<RelicView> relics, DateTime now)
    {
        if (_picked is not { } picked || ctx.IsMultiplayer || now - _pickedAt < PickStall) return;
        if (_pickingDone?.IsCompleted == true || me.Relics.Count() > _relicCountAtPick) return;
        if (!relics.Any(r => r.Enabled)) return;

        ModEntry.Log($"treasure room: picking {picked} had no effect after {PickStall.TotalSeconds:0} s; offering the relics again");
        _picked = null;
    }

    // ---------- reading ----------

    private static string IdleReason(GameContext ctx, Node? chest, bool chestOpen, List<RelicView> relics, DateTime now)
    {
        if (_left)
            return ctx.IsMultiplayer ? "left the treasure room; waiting for teammates" : "leaving the treasure room";

        if (!chestOpen)
        {
            if (chest is null) return "can't find the chest in the treasure room";
            if (_chestClicks == 0) return "the chest is not clickable yet";
            return now - _chestClickedAt > ChestStuckAfter
                ? "the chest still looks closed after the bot's click; a human has to open it"
                : "opening the chest";
        }

        if (_picked is { } picked)
        {
            if (_pickingDone?.IsCompleted != true)
                return ctx.IsMultiplayer ? $"picked {picked}; waiting for teammates to pick" : $"taking {picked}";
            return "waiting for the proceed button";
        }

        if (relics.Count == 0)
            return "opening the chest";
        return relics.Any(r => r.Enabled) ? "relics appearing" : "relics not pickable yet";
    }

    private static bool ChestOpen(Node? chest, List<RelicView> relics) =>
        relics.Count > 0
        || _pickingDone?.IsCompleted == true
        || (_chestClicks > 0 && (chest is null || !ChestClickable(chest)));

    /// <summary>One click per room, ever: the recipe is "ForceClick the Chest once".</summary>
    private static bool CanOpen([NotNullWhen(true)] Node? chest, bool chestOpen) =>
        chest is not null
        && !chestOpen
        && !_left
        && _chestClicks == 0
        && ChestClickable(chest)
        && !Clicks.RecentlyClicked(chest);

    private static bool CanPick(DateTime now) =>
        _picked is null && !_left && _relicsSince is { } since && now - since >= RelicSettle;

    /// <summary>
    /// The holders that are drawn, in tree order. Holders are only read once the chest has been
    /// opened (or can't be clicked any more), so nothing set up inside a closed chest leaks.
    /// </summary>
    private static List<RelicView> ReadRelics(NTreasureRoom room, Node? chest)
    {
        var views = new List<RelicView>();
        if (chest is not null && _chestClicks == 0 && ChestClickable(chest)) return views;

        foreach (var holder in Nodes.FindAll<NTreasureRoomRelicHolder>(room))
        {
            if (!Nodes.Alive(holder) || !Nodes.IsShown(holder)) continue;
            var n = views.Count;
            var relic = RelicOf(holder);
            JsonObject json;
            string name;
            if (relic is not null)
            {
                name = Describe.Text(relic.Title);
                json = Describe.Lead("n", n, Describe.Relic(relic));
            }
            else
            {
                if (!_loggedUnreadableRelic)
                {
                    _loggedUnreadableRelic = true;
                    ModEntry.LogError($"treasure room: can't read the relic on {holder.GetType().Name}");
                }
                name = $"#{n} (unreadable)";
                json = new JsonObject { ["n"] = n, ["name"] = "(unreadable relic)" };
            }

            var enabled = Enabled(holder);
            if (!enabled) json["available"] = false;
            views.Add(new RelicView(n, holder, name, enabled, json));
        }
        return views;
    }

    private static RelicModel? RelicOf(Node holder)
    {
        foreach (var member in HolderRelicMembers)
        {
            var value = Nodes.TryGetPrivate(holder, member);
            if (value is RelicModel model) return model;
            if (value is not null && Nodes.TryGetPrivate(value, "Model") is RelicModel inner) return inner;
        }
        return null;
    }

    private static JsonObject BuildState(GameContext ctx, RunState rs, Player me, List<RelicView> relics, bool chestOpen)
    {
        var player = Describe.PlayerRun(me, includeDeck: true);
        player.Remove("potions"); // replaced by the detailed slots below

        var state = new JsonObject
        {
            ["run"] = Describe.RunInfo(rs),
            ["chest_open"] = chestOpen,
        };

        if (relics.Count > 0)
        {
            var list = new JsonArray();
            foreach (var r in relics) list.Add(r.Json);
            state["relics"] = list;
        }

        if (_picked is { } picked)
        {
            state["picked"] = picked;
            if (_pickingDone is { } task) state["relic_awarded"] = task.IsCompleted;
        }

        if (ctx.IsMultiplayer && relics.Count > 0)
            state["co_op"] = "each player picks one relic; teammates' picks aren't read by the bot";

        state["player"] = player;
        state["potions"] = PotionActions.Describe(ctx, false);
        return state;
    }

    // ---------- the chest ----------

    /// <summary>The room's "Chest" node, found once per room (a direct child, a unique name, or anywhere below).</summary>
    private static Node? Chest(NTreasureRoom room)
    {
        if (_chest is { } cached && Nodes.Alive(cached)) return cached;

        _chest = Nodes.Get<Node>(room, "Chest")
            ?? Nodes.Get<Node>(room, "%Chest")
            ?? room.FindChild("Chest", true, false);

        if (_chest is null && !_loggedNoChest && DateTime.UtcNow - _firstSeen > Settle)
        {
            _loggedNoChest = true;
            ModEntry.LogError("treasure room: no node named Chest");
        }
        return _chest;
    }

    // VERIFY: the "Chest" node stops reporting IsEnabled once opened (by us or, in co-op, by a teammate); while it reports enabled the bot treats it as closed (no relic read before our click, no proceed)
    private static bool ChestClickable(Node chest) =>
        Nodes.Alive(chest) && Nodes.IsShown(chest) && Enabled(chest);

    // ---------- helpers ----------

    /// <summary>
    /// Enabled the way the game's buttons report it. Taking a plain Node keeps this independent
    /// of which button class the chest and the relic holders derive from.
    /// VERIFY: the "Chest" node and NTreasureRoomRelicHolder derive from NClickableControl (IsEnabled, ForceClick)
    /// </summary>
    private static bool Enabled(Node node) => node switch
    {
        NClickableControl c => c.IsEnabled,
        Godot.BaseButton b => !b.Disabled,
        _ => Nodes.TryGetPrivate(node, "IsEnabled") is not false,
    };

    private static void Press(Node node)
    {
        switch (node)
        {
            case NClickableControl c:
                c.ForceClick();
                break;
            case Godot.BaseButton b:
                b.EmitSignal(Godot.BaseButton.SignalName.Pressed);
                break;
            default:
                Nodes.CallPrivate(node, "ForceClick");
                break;
        }
    }

    private static NTreasureRoom? Room(Node? node)
    {
        var room = node as NTreasureRoom ?? NRun.Instance?.TreasureRoom;
        return room is not null && Nodes.Alive(room) ? room : null;
    }

    private static bool CanClick([NotNullWhen(true)] NProceedButton? button) =>
        button is not null && Nodes.Alive(button) && Nodes.IsShown(button) && button.IsEnabled && !Clicks.RecentlyClicked(button);
}
