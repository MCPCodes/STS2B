using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Game.Coop;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// The act map while it is open: where we are, which points we can travel to next, and a
/// compact description of the rest of the act so the model can plan a route. Travel is a
/// click on the map point, which the game turns into a synced map vote (docs/game-api.md
/// section d), so co-op stays in step. Actions: "map:&lt;n&gt;" for state.next[n], plus
/// out-of-combat potion actions.
///
/// In co-op the click is a vote, and the reader follows <see cref="CoopManners.Vote"/>: it shows
/// who voted for what, waits for the human's vote before offering ours, lets a standing vote be
/// changed until the round is decided, and offers nothing else once our vote is out. One more
/// action exists there: "stop_drawing", when map drawing mode was left on.
/// </summary>
internal sealed class MapReader : IScreen
{
    public static readonly MapReader Instance = new();

    public string Name => "map";

    private static readonly TimeSpan TravelTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Give a co-op vote time to reach the action queue before "idle" can mean "registered".</summary>
    private static readonly TimeSpan VoteSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>Room types counted along routes for the per-option summary, in display order.</summary>
    private static readonly string[] Tally = ["monster", "elite", "unknown", "shop", "rest", "treasure"];

    // The bridge serializes with the default JSON encoder, which turns < > ' + into \uXXXX
    // escapes, so the map strings avoid those characters.
    private const string MapKey =
        "rows go upward by row number; each node is cCOL:TYPE^CHILD_COLS (children are in the next row); " +
        "@ marks you; (quest) marks a quest target. next[].ahead: min-max count of each room type over the " +
        "routes from that node to the boss, itself included.";

    private MapReader() { }

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        var screen = node as NMapScreen ?? NMapScreen.Instance;
        if (screen is null || !GodotObject.IsInstanceValid(screen) || !screen.IsOpen)
            return GameSnapshot.Busy(Name, "map closing");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return GameSnapshot.Busy(Name, "run loading");
        if (screen.IsTraveling)
            return GameSnapshot.Busy(Name, "travelling on the map");
        if (CombatManager.Instance?.IsInProgress == true)
            return GameSnapshot.Busy(Name, "map opened during combat; close it to continue");

        MapView view;
        try
        {
            view = MapView.Read(rs);
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"reading map: {ex.Message}");
            return GameSnapshot.Busy(Name, "map not readable yet");
        }
        // NullActMap (before generation) still yields its placeholder start and boss at (0,0).
        if (rs.Map is NullActMap || view.Spots.Count == 0)
            return GameSnapshot.Busy(Name, "map not generated yet");

        var state = BuildState(ctx, rs, me, view);

        // Co-op has its own flow (who voted, whom to follow, what may still be sent); when that
        // read fails it answers null and the plain flow below takes over.
        if (Teammates.Active(ctx) && CoopCapture(ctx, rs, me, screen, view, state) is { } coopSnapshot)
            return coopSnapshot;

        // Our vote first: travel stays enabled after voting (another click would change the vote).
        string? waiting = null;
        if (CurrentVote(rs) is { } vote)
            waiting = $"waiting for teammates to vote on the map (you voted {Show(vote.Target)})";
        else if (RoomUnfinished(ctx, rs) is { } unfinished)
            waiting = $"the map was opened by hand while {unfinished}; close the map so the room can be finished";
        else if (!screen.IsTravelEnabled)
            waiting = "map open but travel not enabled (viewing only, or still animating)";
        else if (screen.Drawings.GetLocalDrawingMode() != DrawingMode.None)
            waiting = "map drawing mode is on; the game ignores map point clicks until it is turned off";

        var actions = new List<LegalAction>();
        if (waiting is null)
        {
            string? blocked = null;
            for (var i = 0; i < view.Next.Count; i++)
            {
                var at = view.Next[i];
                if (FindPointNode(screen, rs.Map, at, fresh: false) is not { } p) continue;
                if (Blocked(screen, p) is { } why)
                    blocked ??= $"{Show(at)} {why}";
                else
                    actions.Add(new LegalAction($"map:{i}", $"Go to {Label(view.TypeAt(at))} at {Show(at)}"));
            }
            // Standing on the act's last boss: the act ends through the rewards screen, so a map
            // opened from the top bar now only blocks it.
            if (view.Next.Count == 0)
                waiting = "nowhere left to travel on this map; close the map to continue";
            else if (actions.Count == 0)
                waiting = blocked is null ? "no map point is selectable yet" : $"no map point is selectable yet: {blocked}";
        }

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        actions.AddRange(PotionActions.Legal(ctx, false));
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    /// <summary>
    /// Why travelling now would abandon the current room, or null when it's done. The bot
    /// opens the map only through a room's proceed button or an event's Leave (recorded in
    /// <see cref="Latches.ProceededFrom"/>); a map opened by hand from the top bar must not
    /// walk away from unclaimed rewards, a shop, a rest site or an event mid-way.
    /// </summary>
    private static string? RoomUnfinished(GameContext ctx, RunState rs)
    {
        var room = rs.CurrentRoom;
        if (room is null || rs.VisitedMapCoords.Count == 0) return null; // act start: nothing to finish
        if (ReferenceEquals(Latches.ProceededFrom, room)) return null;

        switch (room.RoomType)
        {
            case RoomType.Map:
                return null;
            case RoomType.Monster or RoomType.Elite or RoomType.Boss:
                if (ctx.InCombat) return "a fight is on";
                // Rewards screens stay on the overlay stack under the map; any claimable button means loot left behind.
                foreach (var b in Nodes.FindAll<NRewardButton>(NOverlayStack.Instance))
                {
                    if (Nodes.Alive(b) && b.IsEnabled) return "rewards are still unclaimed";
                }
                return null;
            case RoomType.Event:
                try { return RunManager.Instance.EventSynchronizer.GetLocalEvent()?.IsFinished == true ? null : "the event isn't finished"; }
                catch { return "the event isn't finished"; }
            case RoomType.RestSite:
                return NRestSiteRoom.Instance?.ProceedButton is { IsEnabled: true } ? null : "no rest site option was chosen";
            case RoomType.Shop:
                return "in the shop";
            case RoomType.Treasure:
                return "in the treasure room";
            default:
                return null;
        }
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        // Co-op only: the action that ends a drawing mode left on, and the rule that nothing,
        // not even a potion, is sent once our map vote is out.
        var coop = Teammates.Active(ctx);
        if (coop && actionId == StopDrawingId)
            return StopDrawing(node as NMapScreen ?? NMapScreen.Instance);
        if (coop && !actionId.StartsWith("map:", StringComparison.Ordinal) && VoteStanding(ctx))
            return ActionResult.Fail($"Not now: {CoopManners.RoomClosed}.");

        if (PotionActions.TryExecute(ctx, actionId, false, out var potionResult))
            return potionResult;

        if (!actionId.StartsWith("map:", StringComparison.Ordinal) || !int.TryParse(actionId.AsSpan(4), out var n))
            return ActionResult.Fail($"Unknown map action '{actionId}'.");

        if (ctx.Run is not { } rs)
            return ActionResult.Fail("No run in progress.");

        var screen = node as NMapScreen ?? NMapScreen.Instance;
        if (screen is null || !GodotObject.IsInstanceValid(screen) || !screen.IsOpen)
            return ActionResult.Fail("The map is not open.");
        if (screen.IsTraveling)
            return ActionResult.Fail("Already travelling.");
        if (!screen.IsTravelEnabled)
            return ActionResult.Fail("Travel is not enabled on the map yet.");
        // VoteForMapCoordAction is NonCombat: enqueued mid-combat it would hold up our queue.
        if (CombatManager.Instance?.IsInProgress == true)
            return ActionResult.Fail("A combat is in progress.");
        // The same while a fight is being set up, when it can keep the fight from starting on
        // a copy that is still loading the room (votes.md 0.2 item 12). Travel is off then
        // anyway; this keeps the co-op vote from depending on that.
        if (coop && Teammates.FightStarting(ctx))
            return ActionResult.Fail("A combat is starting.");
        // Nothing of ours may still be in flight when the vote goes out (votes.md list E item 14).
        if (coop && ctx.Me is { } owner && PotionInFlight(owner, DateTime.UtcNow))
            return ActionResult.Fail("A potion is still being used; vote once it is gone from the belt.");
        if (screen.Drawings.GetLocalDrawingMode() != DrawingMode.None)
            return ActionResult.Fail("Map drawing mode is on; the game ignores map point clicks.");
        // A co-op vote can be changed until the last one lands; the same node again is refused below.
        if (!coop && CurrentVote(rs) is { } vote)
            return ActionResult.Fail($"Already voted for {Show(vote.Target)}; waiting for teammates.");
        if (RoomUnfinished(ctx, rs) is { } unfinished)
            return ActionResult.Fail($"The current room isn't finished ({unfinished}).");

        MapView view;
        try
        {
            view = MapView.Read(rs);
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"Could not read the map: {ex.Message}");
        }

        if (n < 0 || n >= view.Next.Count)
            return ActionResult.Fail($"There is no map option {n}.");

        var target = view.Next[n];
        // Clicking the node our vote is on casts nothing: the game sends a map ping instead.
        if (coop && ctx.Me is { } voter && StandingVote(rs, voter) == target)
            return ActionResult.Fail($"Your vote is already on {Show(target)}.");

        var point = FindPointNode(screen, rs.Map, target, fresh: true);
        if (point is null)
            return ActionResult.Fail($"No map point node at {Show(target)}.");
        if (Blocked(screen, point) is { } why)
            return ActionResult.Fail($"The map point at {Show(target)} {why}.");

        var what = $"travel to {Label(view.TypeAt(target))} at {Show(target)}";
        var click = Clicks.Once(point, () => point.ForceClick(), $"map point {Show(target)}");
        if (!click.Ok)
            return click;

        // With more than one player the click only casts our vote: the host moves the party once
        // every player slot holds a vote (a lone player's vote decides at once).
        var multiplayer = rs.Players.Count > 1;

        // OnMapPointSelectedLocally records our vote on the screen as the click lands. Without it
        // NMapPoint.OnRelease turned the click down, and latching a vote would wait forever.
        if (multiplayer
            && !(ctx.Me is { } me && screen.PlayerVoteDictionary.TryGetValue(me, out var voted)
                 && voted is { } votedAt && Key(votedAt) == target))
            return ActionResult.Fail($"The game ignored the click on map point {Show(target)}.");

        var roomBefore = rs.CurrentRoom;
        if (multiplayer)
            _vote = new Vote(rs.Map, roomBefore, view.Visited.Count, target, DateTime.UtcNow);
        if (coop)
            ModEntry.Log($"coop: voted for {Label(view.TypeAt(target))} at {Show(target)} on the map");

        ActionTracker.Begin(
            what,
            c => Arrived(c.Run, target, roomBefore) || (multiplayer && ActionTracker.GameLooksIdle()),
            TravelTimeout,
            minWait: multiplayer ? VoteSettle : null);
        return ActionResult.Success;
    }

    // ---------- state ----------

    private static JsonObject BuildState(GameContext ctx, RunState rs, Player me, MapView view)
    {
        // Potion actions are offered here, so show what each potion does (as the rewards and
        // rest readers do) instead of the bare names.
        var player = Describe.PlayerRun(me, includeDeck: true);
        player.Remove("potions");
        var state = new JsonObject
        {
            ["run"] = Describe.RunInfo(rs),
            ["player"] = player,
            ["potions"] = PotionActions.Describe(ctx, false),
        };

        // Co-op routes should suit the weakest deck, so teammates' HP matters here.
        if (Teammates.Team(ctx) is { } coopTeam)
            state["team"] = coopTeam;
        else if (ctx.IsMultiplayer && Describe.TeamHp(ctx.Teammates) is { Count: > 0 } team)
            state["team"] = team;

        Describe.AddBosses(state, rs);

        var act = rs.Act;
        if (act is not null)
        {
            // Only useful before we reach it; afterwards the event itself is on screen. The start
            // node shows the Ancient's icon only when it is Ancient-typed (act 1 without Neow
            // retypes it to Monster).
            if (view.At is null && rs.Map.StartingMapPoint.PointType == MapPointType.Ancient)
            {
                var ancient = Describe.SafeText(() => act.Ancient?.Title);
                if (ancient.Length > 0) state["ancient"] = ancient;
            }
        }

        if (view.At is { } here)
            state["at"] = $"{Show(here)} {view.TypeAt(here)}";

        var next = new JsonArray();
        for (var i = 0; i < view.Next.Count; i++)
        {
            var at = view.Next[i];
            var option = new JsonObject { ["n"] = i, ["at"] = Show(at), ["type"] = view.TypeAt(at) };
            var ahead = view.Ahead(at);
            if (ahead.Length > 0) option["ahead"] = ahead;
            next.Add(option);
        }
        state["next"] = next;
        state["map"] = view.ToJson();
        return state;
    }

    // ---------- co-op vote ----------

    /// <summary>Our co-op map vote, valid until the room, the visited path or the map changes.</summary>
    private sealed record Vote(object Map, object? Room, int VisitedCount, (int Col, int Row) Target, DateTime At);

    private static Vote? _vote;

    private static Vote? CurrentVote(RunState rs)
    {
        if (_vote is { } v
            && (!ReferenceEquals(v.Map, rs.Map)
                || !ReferenceEquals(v.Room, rs.CurrentRoom)
                || v.VisitedCount != rs.VisitedMapCoords.Count()))
        {
            _vote = null;
        }
        return _vote;
    }

    // ---------- co-op: the vote round ----------

    private const string StopDrawingId = "stop_drawing";

    /// <summary>
    /// A vote the host has not echoed after this long was dropped (it logs a warning and tells
    /// nobody): our screen then shows a vote the host never recorded, and clicking the same
    /// node again only pings (votes.md 1.2).
    /// </summary>
    private static readonly TimeSpan VoteLost = TimeSpan.FromSeconds(8);

    /// <summary>How long we have waited for a human to vote first, and held a deciding vote for a busy one.</summary>
    private static readonly CoopManners.WaitClock VoteWait = new();
    private static readonly CoopManners.WaitClock BusyWait = new();

    /// <summary>What a vote round is keyed on: the votes are cleared at every move, and cancelled when the map regenerates.</summary>
    private sealed record RoundKey(object? Room, int Visited, object Map);

    /// <summary>
    /// Where our own vote stands: the click we made (the host's echo follows a moment later on
    /// a client), else what the game has recorded for us. Null when we have not voted.
    /// </summary>
    private static (int Col, int Row)? StandingVote(RunState rs, Player me)
    {
        if (CurrentVote(rs) is { } pending) return pending.Target;
        return Teammates.MapVote(me) is { } echoed ? Key(echoed) : null;
    }

    /// <summary>
    /// True once our map vote is out in a co-op run. From then on the room is closed for us:
    /// the other readers offer nothing until the party has moved (see <see cref="Teammates.RoomClosed"/>).
    /// </summary>
    internal static bool VoteStanding(GameContext ctx) =>
        Teammates.Active(ctx) && ctx.Run is { } rs && ctx.Me is { } me && StandingVote(rs, me) is not null;

    /// <summary>
    /// The map in a co-op run, or null when the co-op read failed (the plain flow then answers).
    /// Adds who voted for each option and the "vote" block to <paramref name="state"/>.
    /// Safe on a client and on the host: it only reads, except for un-sticking a vote the host
    /// dropped, which touches nothing but this screen's own vote icons.
    /// </summary>
    private static GameSnapshot? CoopCapture(GameContext ctx, RunState rs, Player me, NMapScreen screen, MapView view, JsonObject state) =>
        Teammates.Safe<GameSnapshot?>("the map vote", null, () =>
        {
            var now = DateTime.UtcNow;
            ForgetDroppedVote(rs, me, screen, now);

            var players = Teammates.All(ctx);
            // The players whose vote we follow: the humans, on a bot's copy; nobody anywhere else.
            var humans = players.Where(m => m.Followed).ToList();
            var mine = StandingVote(rs, me);

            // Each player's vote as the game has it; ours as it stands on this screen.
            var votes = new Dictionary<int, (int Col, int Row)>();
            foreach (var m in players)
            {
                (int Col, int Row)? at = m.IsMe ? mine : Teammates.MapVote(m.Player) is { } c ? Key(c) : null;
                if (at is { } voted) votes[m.Slot] = voted;
            }
            var missing = players.Where(m => !m.IsMe && !votes.ContainsKey(m.Slot)).ToList();

            // state.next[n].votes: who is on each option.
            if (state["next"] is JsonArray next)
            {
                for (var i = 0; i < view.Next.Count && i < next.Count; i++)
                {
                    var voters = players.Where(m => votes.TryGetValue(m.Slot, out var at) && at == view.Next[i])
                        .Select(m => (JsonNode)(m.IsMe ? "you" : m.Label)).ToArray();
                    if (voters.Length > 0 && next[i] is JsonObject option) option["votes"] = new JsonArray(voters);
                }
            }

            int? Option((int Col, int Row)? at) => at is { } a ? view.Next.IndexOf(a) : null;
            var humanVotes = humans.Select(h => Option(votes.TryGetValue(h.Slot, out var at) ? at : null)).ToList();
            var noHumanVoted = humanVotes.All(v => v is null);

            // The wait for a human's vote only counts while one of them is looking at the map;
            // the hold on a deciding vote lasts while one who voted is busy on another screen.
            var key = new RoundKey(rs.CurrentRoom, view.Visited.Count, rs.Map);
            var unvoted = humans.Where(h => !votes.ContainsKey(h.Slot)).ToList();
            var looking = unvoted.Any(h => Teammates.Where(h) is Teammates.Presence.AtMap or Teammates.Presence.Unknown);
            var votedHumans = humans.Where(h => votes.ContainsKey(h.Slot)).ToList();
            var busy = votedHumans.Any(h => Teammates.Where(h) == Teammates.Presence.Busy);
            var unsure = votedHumans.Any(h => Teammates.Where(h) is Teammates.Presence.InRoom or Teammates.Presence.Unknown);

            var advice = CoopManners.Vote(new CoopManners.VoteFacts(
                Mine: Option(mine),
                Humans: humanVotes,
                OthersMissing: missing.Count,
                WaitedForVote: VoteWait.Tick(key, mine is null && noHumanVoted && looking, now),
                Patience: CoopManners.Patience,
                WaitedForBusy: BusyWait.Tick(key, mine is null && !noHumanVoted && missing.Count == 0, now),
                HoldFor: CoopManners.HoldFor(busy, unsure, CoopManners.Patience)));

            var who = Teammates.Names(humans);
            var theirs = advice.Option is { } k && k >= 0 && k < view.Next.Count ? view.Next[k] : ((int Col, int Row)?)null;
            var line = CoopManners.VoteLine(
                advice.Move, who,
                theirs is { } t ? $"{Label(view.TypeAt(t))} at {Show(t)}" : null,
                mine is { } m0 ? $"{Label(view.TypeAt(m0))} at {Show(m0)}" : null);

            var vote = new JsonObject
            {
                ["rule"] = CoopManners.MapRule,
                ["voted"] = CoopManners.Count(votes.Count, players.Count, "have voted",
                    missing.Select(m => m.Label).Concat(mine is null ? ["you"] : []).ToList()),
            };
            if (mine is { } standing) vote["yours"] = Show(standing);
            state["vote"] = vote;

            // The same gates as a solo vote, in the same words. A standing vote skips the room
            // check: the room was finished when it was cast.
            string? waiting = null;
            var actions = new List<LegalAction>();
            if (mine is null && RoomUnfinished(ctx, rs) is { } unfinished)
                waiting = $"the map was opened by hand while {unfinished}; close the map so the room can be finished";
            else if (!screen.IsTravelEnabled)
                waiting = "map open but travel not enabled (viewing only, or still animating)";
            else if (Teammates.FightStarting(ctx))
                waiting = CoopManners.FightStarting;
            else if (mine is null && PotionInFlight(me, now))
                // The potion and the vote run in the order sent; the vote still waits for the
                // potion to leave the belt, so nothing of ours is in flight when the party moves.
                waiting = "a potion is still being used; the map vote comes after it";
            else if (screen.Drawings.GetLocalDrawingMode() != DrawingMode.None)
                // Nobody touches a bot copy's window, but a mode left on would hold the group for good.
                actions.Add(new LegalAction(StopDrawingId, "Turn map drawing mode off (the game ignores map clicks while it is on)"));
            else if (view.Next.Count == 0)
                waiting = "nowhere left to travel on this map; close the map to continue";
            else if (!advice.Decision)
                waiting = advice.Move == CoopManners.VoteMove.Keep
                    ? $"waiting for teammates to vote on the map (you voted {Show(mine ?? default)})"
                    : advice.Move == CoopManners.VoteMove.WaitForHuman && CoopManners.Patience != TimeSpan.MaxValue
                        ? $"{line} (the bot votes on its own after {CoopManners.Patience.TotalSeconds:0} s of them looking at the map)"
                        : line;
            else
            {
                string? blocked = null;
                for (var i = 0; i < view.Next.Count; i++)
                {
                    var at = view.Next[i];
                    if (mine == at) continue; // clicking it again only pings
                    if (FindPointNode(screen, rs.Map, at, fresh: false) is not { } p) continue;
                    if (Blocked(screen, p) is { } why)
                    {
                        blocked ??= $"{Show(at)} {why}";
                        continue;
                    }

                    var label = mine is null
                        ? $"Go to {Label(view.TypeAt(at))} at {Show(at)}"
                        : $"Change your vote to {Label(view.TypeAt(at))} at {Show(at)}";
                    if (theirs == at) label += $" (follows {who}: unanimous, so certain)";
                    else if (theirs is not null) label += $" (against the vote of {who}: the host draws between the votes)";

                    var action = new LegalAction($"map:{i}", label);
                    if (theirs == at)
                    {
                        actions.Insert(0, action);
                        vote["follow"] = action.Id;
                    }
                    else actions.Add(action);
                }

                if (actions.Count == 0)
                    waiting = mine is { } kept
                        ? $"waiting for teammates to vote on the map (you voted {Show(kept)})"
                        : blocked is null ? "no map point is selectable yet" : $"no map point is selectable yet: {blocked}";
                else if (theirs is { } wanted && vote["follow"] is null && CannotFollowYet(key, wanted, mine is not null, now))
                    // Following is the whole point; don't offer only the votes against it.
                    waiting = $"{who} voted for {Show(wanted)}, which is not clickable on this screen yet{(blocked is null ? "" : $": {blocked}")}";
                else if (theirs is { } refused && vote["follow"] is null)
                {
                    // Held out long enough: without a vote of ours nobody travels at all.
                    vote["do"] = CoopManners.CannotFollowLine(who, $"{Label(view.TypeAt(refused))} at {Show(refused)}");
                    actions.AddRange(PotionActions.Legal(ctx, false));
                }
                else
                {
                    vote["do"] = line;
                    // A potion before the vote is fine; none may follow it (votes.md list E item 14).
                    if (mine is null) actions.AddRange(PotionActions.Legal(ctx, false));
                }
            }

            return waiting is not null
                ? new GameSnapshot { Screen = Instance.Name, AwaitingDecision = false, Waiting = waiting, State = state }
                : new GameSnapshot { Screen = Instance.Name, AwaitingDecision = true, State = state, Actions = actions };
        });

    /// <summary>How long the human's voted node has been refusing our click in this round.</summary>
    private static readonly CoopManners.WaitClock FollowWait = new();
    private static object? _cannotFollowLogged;

    /// <summary>
    /// True while the bot should keep waiting for the node the human voted for to become
    /// clickable here. With a vote of ours standing there is nothing to give up: the group is
    /// not waiting for us. Without one, the wait ends after <see cref="CoopManners.FollowBlockedCap"/>,
    /// counted only while this is the reason nothing is offered, and the other nodes are offered.
    /// </summary>
    private static bool CannotFollowYet(RoundKey round, (int Col, int Row) wanted, bool voted, DateTime now)
    {
        if (voted) return true;
        var key = (round, wanted);
        if (FollowWait.Tick(key, true, now) < CoopManners.FollowBlockedCap) return true;
        if (!Equals(_cannotFollowLogged, key))
        {
            _cannotFollowLogged = key;
            ModEntry.Log($"coop: the map point {Show(wanted)} the human voted for has refused clicks here for {CoopManners.FollowBlockedCap.TotalSeconds:0} s; offering the other points so the group is not held");
        }
        return false;
    }

    /// <summary>
    /// The longest the vote waits for a used potion to leave the belt. The game marks a potion
    /// as queued when its use is requested and only takes the mark off when the use is
    /// cancelled, so a request the host never ordered would leave it on for good.
    /// </summary>
    private static readonly TimeSpan PotionHold = TimeSpan.FromSeconds(10);

    private static DateTime? _potionQueuedSince;
    private static bool _potionHoldLogged;

    /// <summary>One of our potions has been used and has not left the belt yet (for at most <see cref="PotionHold"/>).</summary>
    private static bool PotionInFlight(Player me, DateTime now)
    {
        var queued = false;
        try
        {
            for (var slot = 0; slot < me.MaxPotionCount && !queued; slot++)
                queued = me.GetPotionAtSlotIndex(slot) is { IsQueued: true };
        }
        catch
        {
            // A belt that can't be read holds nothing back.
        }

        if (!queued)
        {
            _potionQueuedSince = null;
            _potionHoldLogged = false;
            return false;
        }

        if (now - (_potionQueuedSince ??= now) < PotionHold) return true;
        if (!_potionHoldLogged)
        {
            _potionHoldLogged = true;
            ModEntry.Log($"coop: a potion has shown as in use for {PotionHold.TotalSeconds:0} s; not holding the map vote for it any longer");
        }
        return false;
    }

    /// <summary>
    /// Un-sticks a vote the host never recorded: our click put it on this screen, but no echo
    /// came. The screen's entry is put back to what the host has (nothing, or our earlier vote)
    /// so the node can be clicked again. A retry of a vote that was only late is harmless: the
    /// second overwrites the first, and one arriving after the move is dropped.
    /// </summary>
    private static void ForgetDroppedVote(RunState rs, Player me, NMapScreen screen, DateTime now)
    {
        if (CurrentVote(rs) is not { } pending || screen.IsTraveling || now - pending.At < VoteLost) return;

        var echoed = Teammates.MapVote(me);
        if (echoed is { } e && Key(e) == pending.Target) return;

        if (echoed is { } earlier) screen.PlayerVoteDictionary[me] = earlier;
        else screen.PlayerVoteDictionary.Remove(me);
        screen.RefreshAllMapPointVotes();
        _vote = null;
        ModEntry.Log($"coop: the host never recorded the map vote for {Show(pending.Target)}; cleared it here so the bot can vote again");
    }

    /// <summary>
    /// Turns local map drawing mode off through the game's own call (what the map's draw button
    /// does; it also tells the other copies, which only changes a cursor there).
    /// </summary>
    private static ActionResult StopDrawing(NMapScreen? screen)
    {
        if (screen is null || !GodotObject.IsInstanceValid(screen) || !screen.IsOpen)
            return ActionResult.Fail("The map is not open.");

        var drawings = screen.Drawings;
        if (drawings.GetLocalDrawingMode() == DrawingMode.None)
            return ActionResult.Fail("Map drawing mode is already off.");

        // An open line reads as drawing whatever the mode says, so close it first.
        if (drawings.IsLocalDrawing()) drawings.StopLineLocal();
        drawings.SetDrawingModeLocal(DrawingMode.None);
        ModEntry.Log("coop: turned map drawing mode off so map clicks count again");
        return drawings.GetLocalDrawingMode() == DrawingMode.None
            ? ActionResult.Success
            : ActionResult.Fail("Map drawing mode is still on.");
    }

    // ---------- travel ----------

    /// <summary>
    /// Arrival per docs section d, plus a room change: while the map is open CurrentRoom is
    /// still the room we are leaving (non-null), so the documented test alone could pass if
    /// the coord updates before travel starts.
    /// </summary>
    private static bool Arrived(RunState? rs, (int Col, int Row) target, object? roomBefore)
    {
        if (rs is null) return false;
        MapCoord? now = rs.CurrentMapCoord;
        return now is { } c
            && Key(c) == target
            && rs.CurrentRoom is { } room
            && !ReferenceEquals(room, roomBefore)
            && NMapScreen.Instance?.IsTraveling != true;
    }

    // NMapPoint nodes of the open map, cached per map screen and map generation so the
    // ~150 ms poll doesn't walk the whole map scene; any miss falls back to a fresh walk.
    private static ulong _nodesScreenId;
    private static object? _nodesMap;
    private static List<NMapPoint> _nodes = new();

    private static NMapPoint? FindPointNode(NMapScreen screen, ActMap map, (int Col, int Row) at, bool fresh)
    {
        if (map.GetPoint(new MapCoord(at.Col, at.Row)) is not { } want)
            return null;

        var screenId = screen.GetInstanceId();
        var stale = fresh || _nodesScreenId != screenId || !ReferenceEquals(_nodesMap, map);
        if (!stale && MatchNode(want) is { } cached)
            return cached;

        _nodes = Nodes.FindAll<NMapPoint>(screen);
        _nodesScreenId = screenId;
        _nodesMap = map;
        return MatchNode(want);
    }

    // SetMap queue-frees the old nodes and builds new ones on the same MapPoints (MapCmd re-sets
    // the same map), so skip nodes queued for deletion and match the current map's point itself.
    private static NMapPoint? MatchNode(MapPoint want)
    {
        foreach (var p in _nodes)
            if (Nodes.Alive(p) && ReferenceEquals(p.Point, want)) return p;
        return null;
    }

    /// <summary>
    /// Why the game would ignore a click on this point, mirroring NMapPoint.OnRelease (which
    /// drops a refused click silently); null when the click would cast a vote. Drawing mode
    /// is checked with the screen. State is the live half of OnRelease's IsTravelable, IsEnabled
    /// the cached one; both are current while the map is open.
    /// </summary>
    private static string? Blocked(NMapScreen screen, NMapPoint p)
    {
        if (p.State != MapPointState.Travelable || !p.IsEnabled)
            return "is not selectable right now";
        if (p.Point.coord.row == 0 && !SaveManager.Instance.SeenFtue(NMapSelectFtue.id))
            return "waits for the map tutorial popup";
        if (NControllerManager.Instance?.IsUsingController == true && !screen.IsNodeOnScreen(p))
            return "is off screen while a controller is in use (scroll the map to it)";
        return null;
    }

    // ---------- map data ----------

    private static (int Col, int Row) Key(MapCoord c) => (c.col, c.row);

    private static (int Col, int Row) Key(MapPoint p) => Key(p.coord);

    private static string Show((int Col, int Row) at) => $"({at.Col},{at.Row})";

    private static string TypeWord(MapPointType type) => type switch
    {
        MapPointType.Monster => "monster",
        MapPointType.Elite => "elite",
        MapPointType.RestSite => "rest",
        MapPointType.Shop => "shop",
        MapPointType.Treasure => "treasure",
        MapPointType.Unknown => "unknown",
        MapPointType.Boss => "boss",
        MapPointType.Ancient => "ancient",
        _ => type.ToString().ToLowerInvariant(),
    };

    private static string Label(string type) => type switch
    {
        "monster" => "Monster",
        "elite" => "Elite",
        "rest" => "Rest Site",
        "shop" => "Shop",
        "treasure" => "Treasure",
        "unknown" => "Unknown (?)",
        "boss" => "Boss",
        "ancient" => "Ancient",
        _ => type,
    };

    // NNormalMapPoint shows its quest icon exactly when Quests is non-empty (Spoils Map, Fur Coat).
    private static bool HasQuest(MapPoint p) => p.Quests.Count > 0;

    private sealed record Spot((int Col, int Row) At, string Type, List<(int Col, int Row)> Children, bool Quest);

    /// <summary>The act map as plain data, read fresh from the run state on every call.</summary>
    private sealed class MapView
    {
        public required Dictionary<(int Col, int Row), Spot> Spots { get; init; }

        /// <summary>Visited points this act, oldest first; the last one is where we stand.</summary>
        public required List<(int Col, int Row)> Visited { get; init; }

        /// <summary>Travelable points in a fixed order (row, then column); "map:n" indexes this.</summary>
        public required List<(int Col, int Row)> Next { get; init; }

        public required List<(int Col, int Row)> Bosses { get; init; }

        private readonly Dictionary<(int Col, int Row), (int[] Min, int[] Max)> _ahead = new();

        public (int Col, int Row)? At => Visited.Count > 0 ? Visited[^1] : null;

        public string TypeAt((int Col, int Row) at) => Spots.TryGetValue(at, out var s) ? s.Type : "unknown";

        public static MapView Read(RunState rs)
        {
            var map = rs.Map;
            var spots = new Dictionary<(int Col, int Row), Spot>();

            void Add(MapPoint? p)
            {
                if (p is null) return;
                var key = Key(p);
                if (spots.ContainsKey(key)) return;
                var children = new List<(int Col, int Row)>();
                foreach (var child in p.Children) children.Add(Key(child));
                children.Sort();
                spots[key] = new Spot(key, TypeWord(p.PointType), children, HasQuest(p));
            }

            Add(map.StartingMapPoint);
            foreach (var p in map.GetAllMapPoints()) Add(p);
            Add(map.BossMapPoint);
            Add(map.SecondBossMapPoint);

            var visitedCoords = rs.VisitedMapCoords.ToList();
            var visited = visitedCoords.Select(c => Key(c)).ToList();

            var bosses = new List<(int Col, int Row)>();
            if (map.BossMapPoint is { } boss) bosses.Add(Key(boss));
            if (map.SecondBossMapPoint is { } second) bosses.Add(Key(second));

            return new MapView
            {
                Spots = spots,
                Visited = visited,
                Next = NextPoints(rs, map, visitedCoords),
                Bosses = bosses,
            };
        }

        /// <summary>Mirrors the game's private RecalculateTravelability (docs section d).</summary>
        private static List<(int Col, int Row)> NextPoints(RunState rs, ActMap map, List<MapCoord> visited)
        {
            var next = new List<(int Col, int Row)>();
            if (visited.Count == 0)
            {
                if (map.StartingMapPoint is { } start) next.Add(Key(start));
                return next;
            }

            var last = visited[^1];
            var lastKey = Key(last);
            var boss = map.BossMapPoint;
            var second = map.SecondBossMapPoint;

            if (boss is not null && Key(boss) == lastKey)
            {
                if (second is not null) next.Add(Key(second));
            }
            else if (second is not null && Key(second) == lastKey)
            {
                // Act over: leaving goes through the rewards screen's proceed button.
            }
            else if (lastKey.Row == map.GetRowCount() - 1)
            {
                if (boss is not null) next.Add(Key(boss));
            }
            else if (map.GetPoint(last) is { } from)
            {
                // Children, or the whole next row under free travel (Winged Boots solo, Flight).
                next.AddRange(MapTravel.GetTravelablePointsFrom(rs, from)
                    .Select(p => Key(p))
                    .Distinct()
                    .OrderBy(k => k.Row)
                    .ThenBy(k => k.Col));
            }
            else
            {
                // The game falls back to the start when the last visited coord is not on the map.
                next.Add(Key(map.StartingMapPoint));
            }
            return next;
        }

        /// <summary>Compact map: visited path plus every node still reachable, row by row upward.</summary>
        public JsonObject ToJson()
        {
            var json = new JsonObject { ["key"] = MapKey };

            if (Visited.Count > 0)
                json["visited"] = string.Join(", ", Visited.Select(k => $"{Show(k)} {TypeAt(k)}"));

            var shown = Reachable();
            var here = At;
            if (here is { } h) shown.Add(h);
            var floorRow = here?.Row ?? 0;
            foreach (var b in Bosses)
                if (b.Row >= floorRow) shown.Add(b);

            var rows = new JsonObject();
            foreach (var row in shown.GroupBy(k => k.Row).OrderBy(g => g.Key))
            {
                var sb = new StringBuilder();
                foreach (var key in row.OrderBy(k => k.Col))
                {
                    if (!Spots.TryGetValue(key, out var spot)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    if (here is { } at && at == key) sb.Append('@');
                    sb.Append('c').Append(key.Col).Append(':').Append(spot.Type);
                    if (spot.Quest) sb.Append("(quest)");
                    if (spot.Children.Count > 0) sb.Append('^').AppendJoin(',', spot.Children.Select(c => c.Col));
                }
                if (sb.Length > 0) rows[row.Key.ToString()] = sb.ToString();
            }
            json["rows"] = rows;
            return json;
        }

        /// <summary>Everything reachable from the next options by following map paths.</summary>
        private HashSet<(int Col, int Row)> Reachable()
        {
            var seen = new HashSet<(int Col, int Row)>();
            var queue = new Queue<(int Col, int Row)>(Next);
            while (queue.Count > 0)
            {
                var key = queue.Dequeue();
                if (!seen.Add(key) || !Spots.TryGetValue(key, out var spot)) continue;
                foreach (var child in spot.Children)
                    if (!seen.Contains(child)) queue.Enqueue(child);
            }
            return seen;
        }

        /// <summary>"monster 3-5, elite 1-2, rest 1": per-type min-max over routes from <paramref name="start"/>.</summary>
        public string Ahead((int Col, int Row) start)
        {
            var (min, max) = Counts(start);
            var parts = new List<string>();
            for (var i = 0; i < Tally.Length; i++)
            {
                if (max[i] == 0) continue;
                parts.Add(min[i] == max[i] ? $"{Tally[i]} {max[i]}" : $"{Tally[i]} {min[i]}-{max[i]}");
            }
            return string.Join(", ", parts);
        }

        private (int[] Min, int[] Max) Counts((int Col, int Row) key)
        {
            if (_ahead.TryGetValue(key, out var memo)) return memo;

            var min = new int[Tally.Length];
            var max = new int[Tally.Length];
            if (Spots.TryGetValue(key, out var spot))
            {
                // Paths only go upward; the row check also guards against a malformed cycle.
                var kids = spot.Children.Where(c => c.Row > key.Row && Spots.ContainsKey(c)).ToList();
                if (kids.Count > 0)
                {
                    Array.Fill(min, int.MaxValue);
                    foreach (var kid in kids)
                    {
                        var (kidMin, kidMax) = Counts(kid);
                        for (var i = 0; i < Tally.Length; i++)
                        {
                            min[i] = Math.Min(min[i], kidMin[i]);
                            max[i] = Math.Max(max[i], kidMax[i]);
                        }
                    }
                }

                var self = Array.IndexOf(Tally, spot.Type);
                if (self >= 0)
                {
                    min[self]++;
                    max[self]++;
                }
            }

            _ahead[key] = (min, max);
            return (min, max);
        }
    }
}
