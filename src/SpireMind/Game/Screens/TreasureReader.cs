using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// A treasure room (NTreasureRoom, a room rather than an overlay): open the chest, take a
/// relic, leave. The chest is the room's "Chest" button, clicked once; its relics sit in
/// NTreasureRoomRelicHolders, and clicking one calls TreasureRoomRelicSynchronizer.PickRelicLocally,
/// which is a vote in co-op (docs/game-api.md section e). Leaving before the relic collection's
/// RelicPickingFinished() Task completes loses the relic, so "proceed" waits for that Task,
/// which is polled and never awaited. A rewards screen the chest pushes is routed to
/// RewardsReader before this reader is asked; the holders are bound only after it closes.
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

    /// <summary>The relics rise out of the chest for up to a second; give them a moment, as a human would.</summary>
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

    private TreasureReader() { }

    /// <summary>One relic on offer as read just now; <see cref="N"/> is its place among the visible holders.</summary>
    private sealed record RelicView(int N, NTreasureRoomRelicHolder Holder, string Name, bool Enabled, JsonObject Json);

    // ---------- per-room memory (reset when a different treasure room shows up) ----------

    private static ulong _roomId;
    private static object? _roomModel;
    private static DateTime _firstSeen;
    private static DateTime _lastCaptured;
    private static DateTime _onScreenSince;
    private static NClickableControl? _chest;
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

            if (CanPick(me, now))
            {
                foreach (var r in relics)
                    if (r.Enabled && !Clicks.RecentlyClicked(r.Holder))
                        actions.Add(new LegalAction($"relic:{r.N}", ctx.IsMultiplayer ? $"Pick relic {r.Name} (co-op vote)" : $"Take relic {r.Name}"));
            }

            if (CanClick(room.ProceedButton) && ProceedSafe(ctx, me, chest, relics, now))
                actions.Add(new LegalAction("proceed", _picked is null && relics.Count == 0 ? "Leave the treasure room (no relic on offer)" : "Leave the treasure room"));

            // Potion actions alone are not a treasure room decision; they ride along with the real ones.
            if (actions.Count > 0)
                actions.AddRange(PotionActions.Legal(ctx, false));
            else
                waiting = IdleReason(ctx, me, chest, chestOpen, relics, now);
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

    private static ActionResult Open(NTreasureRoom room, NClickableControl? found)
    {
        if (found is not { } chest)
            return ActionResult.Fail("Can't find the chest.");
        if (!CanOpen(chest, ChestOpen(chest, ReadRelics(room, chest))))
            return ActionResult.Fail(_chestClicks > 0 ? "The chest was already clicked once; waiting for it to open." : "The chest can't be opened right now.");

        var click = Clicks.Once(chest, () =>
        {
            _chestClicks++;
            _chestClickedAt = DateTime.UtcNow;
            chest.ForceClick();
        }, "the chest");
        if (!click.Ok)
            return click;

        ActionTracker.Begin(
            "opening the chest",
            _ => !Nodes.Alive(room) || ReadRelics(room, chest).Count > 0 || _pickingDone?.IsCompleted == true || Nodes.TopOverlay() is not null || Nodes.ModalOpen(),
            OpenTimeout);
        return ActionResult.Success;
    }

    private static ActionResult Pick(GameContext ctx, NTreasureRoom room, Player me, List<RelicView> relics, int n)
    {
        if (_picked is { } already)
            return ActionResult.Fail($"Already picked {already}.");
        if (!VotingOpen())
            return ActionResult.Fail("The game isn't taking relic votes right now.");
        if (MyVoteIn(me))
            return ActionResult.Fail("A relic vote is already in for this player.");
        if (!CanPick(me, DateTime.UtcNow))
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
                holder.ForceClick();
            }, $"relic {r.Name}");
        }
        catch (Exception ex)
        {
            // Our own failure around the press. PickRelicLocally's throw when voting is closed
            // happens inside the Released signal, where Godot logs it; VotingOpen() above prevents it.
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

    private static ActionResult Proceed(GameContext ctx, RunState rs, Player me, NTreasureRoom room, NClickableControl? chest, List<RelicView> relics)
    {
        if (!ProceedSafe(ctx, me, chest, relics, DateTime.UtcNow))
            return ActionResult.Fail("The relic hasn't been awarded yet; leaving now would lose it.");

        var proceed = room.ProceedButton;
        if (proceed is null || !Nodes.IsShown(proceed))
            return ActionResult.Fail("There is no proceed button.");
        if (!proceed.IsEnabled)
            return ActionResult.Fail("The proceed button is not enabled yet.");
        if (proceed.IsSkip)
            return ActionResult.Fail("The button still says Skip; pressing it would give up the relic.");

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

    /// <summary>Per-room bookkeeping: the game's picking Task, and when the relics appeared.</summary>
    private static void Observe(NTreasureRoom room, List<RelicView> relics)
    {
        LookUpPickingTask(room);
        if (relics.Count > 0)
            _relicsSince ??= DateTime.UtcNow;

        if (_pickingDone is { IsFaulted: true } faulted && !_loggedPickingFault)
        {
            _loggedPickingFault = true;
            ModEntry.LogError($"treasure room: relic picking failed: {faulted.Exception?.GetBaseException().Message}");
        }
    }

    /// <summary>
    /// Fetch the relic collection's RelicPickingFinished() Task once per room. Each room node has
    /// its own collection, whose Task completes once: after the award animation has called
    /// RelicCmd.Obtain for every relic, or about a second after an empty chest opens. So a
    /// completed Task always means this room's picking is over.
    /// </summary>
    private static void LookUpPickingTask(NTreasureRoom room)
    {
        if (_pickingLookedUp) return;
        _pickingLookedUp = true;

        var collection = Nodes.Get<NTreasureRoomRelicCollection>(room, "%RelicCollection")
            ?? Nodes.FindFirst<NTreasureRoomRelicCollection>(room);
        if (collection is null)
        {
            ModEntry.LogError("treasure room: no NTreasureRoomRelicCollection; watching our relics instead");
            return;
        }
        _pickingDone = collection.RelicPickingFinished();
    }

    /// <summary>
    /// True when leaving can't cost us the relic: the game says picking is finished (an empty
    /// chest included); or, if its Task can't be had, our relic count went up or no relic was
    /// ever offered (empty chest).
    /// </summary>
    private static bool ProceedSafe(GameContext ctx, Player me, NClickableControl? chest, List<RelicView> relics, DateTime now)
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
        // The holders stay enabled through the award animation, so ask the game: only a vote that
        // is still open and holds no vote of ours means the press never arrived.
        if (!VotingOpen() || MyVoteIn(me)) return;

        ModEntry.Log($"treasure room: picking {picked} had no effect after {PickStall.TotalSeconds:0} s; offering the relics again");
        _picked = null;
    }

    // ---------- reading ----------

    private static string IdleReason(GameContext ctx, Player me, NClickableControl? chest, bool chestOpen, List<RelicView> relics, DateTime now)
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
        if (MyVoteIn(me))
            return ctx.IsMultiplayer ? "a relic vote the bot didn't make is in; waiting for teammates" : "a relic pick or skip the bot didn't make is in";
        return relics.Any(r => r.Enabled) ? "relics appearing" : "relics not pickable yet";
    }

    private static bool ChestOpen(NClickableControl? chest, List<RelicView> relics) =>
        relics.Count > 0
        || _pickingDone?.IsCompleted == true
        || (_chestClicks > 0 && (chest is null || !ChestClickable(chest)));

    /// <summary>One click per room, ever: the recipe is "ForceClick the Chest once".</summary>
    private static bool CanOpen([NotNullWhen(true)] NClickableControl? chest, bool chestOpen) =>
        chest is not null
        && !chestOpen
        && !_left
        && _chestClicks == 0
        && ChestClickable(chest)
        && !Clicks.RecentlyClicked(chest);

    private static bool CanPick(Player me, DateTime now) =>
        _picked is null && !_left && _relicsSince is { } since && now - since >= RelicSettle && VotingOpen() && !MyVoteIn(me);

    /// <summary>The game is taking relic votes: from room entry until every vote is in. PickRelicLocally throws outside it.</summary>
    private static bool VotingOpen() =>
        RunManager.Instance?.TreasureRoomRelicSynchronizer?.CurrentRelics is not null;

    /// <summary>
    /// While voting is open, a vote is already in for us: the human clicked a relic or (singleplayer)
    /// Skip. The bot then never votes, since a co-op re-vote would overwrite the human's choice.
    /// </summary>
    private static bool MyVoteIn(Player me)
    {
        if (RunManager.Instance?.TreasureRoomRelicSynchronizer is not { CurrentRelics: not null } sync) return false;
        try { return sync.GetPlayerVote(me).voteReceived; }
        catch (ArgumentOutOfRangeException) { return false; } // the vote list is per player at BeginRelicPicking
    }

    /// <summary>
    /// The holders that are drawn, in tree order. Holders are only read once the chest has been
    /// opened (or can't be clicked any more), so nothing set up inside a closed chest leaks. The
    /// relic collection stays hidden until InitializeRelics has bound the holders, so a drawn
    /// holder is always bound to a relic and safe to vote with.
    /// </summary>
    private static List<RelicView> ReadRelics(NTreasureRoom room, NClickableControl? chest)
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

            var enabled = holder.IsEnabled;
            if (!enabled) json["available"] = false;
            views.Add(new RelicView(n, holder, name, enabled, json));
        }
        return views;
    }

    /// <summary>The holder's relic: its NRelic node's Model, which throws until the holder is bound.</summary>
    private static RelicModel? RelicOf(NTreasureRoomRelicHolder holder)
    {
        try { return holder.Relic?.Model; }
        catch (InvalidOperationException) { return null; }
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

    /// <summary>The room's "Chest" button (an NButton the room binds as %Chest), found once per room.</summary>
    private static NClickableControl? Chest(NTreasureRoom room)
    {
        if (_chest is { } cached && Nodes.Alive(cached)) return cached;

        _chest = Nodes.Get<NClickableControl>(room, "%Chest")
            ?? Nodes.Get<NClickableControl>(room, "Chest");

        if (_chest is null && !_loggedNoChest && DateTime.UtcNow - _firstSeen > Settle)
        {
            _loggedNoChest = true;
            ModEntry.LogError("treasure room: no node named Chest");
        }
        return _chest;
    }

    // The room disables the chest in its click handler and nothing re-enables it. Each co-op
    // player has their own chest, so only our click (or the human's) closes it.
    private static bool ChestClickable(NClickableControl chest) =>
        Nodes.Alive(chest) && Nodes.IsShown(chest) && chest.IsEnabled;

    // ---------- helpers ----------

    private static NTreasureRoom? Room(Node? node)
    {
        var room = node as NTreasureRoom ?? NRun.Instance?.TreasureRoom;
        return room is not null && Nodes.Alive(room) ? room : null;
    }

    /// <summary>
    /// In singleplayer the button reads Skip (SkipRelicLocally, forfeiting the relic) from 2.5 s
    /// after the chest opens until picking finishes; the bot never skips.
    /// </summary>
    private static bool CanClick([NotNullWhen(true)] NProceedButton? button) =>
        button is not null && Nodes.Alive(button) && Nodes.IsShown(button) && button.IsEnabled && !button.IsSkip && !Clicks.RecentlyClicked(button);
}
