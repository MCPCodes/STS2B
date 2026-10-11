using System.Text.Json.Nodes;
using SpireMind.Game.Coop;
using SpireMind.Patches;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// A fight from the local player's side: hand, piles, energy, enemies with intents, potions,
/// and the actions to play a card ("play:&lt;i&gt;", "play:&lt;i&gt;&gt;e&lt;j&gt;",
/// "play:&lt;i&gt;&gt;p&lt;j&gt;"), use or discard a potion, or "end_turn". Everything goes
/// through the game's synced action queue so co-op stays in step (docs/game-api.md section c).
///
/// When a card or potion asks for cards from the hand (exhaust, discard, retain, upgrade...)
/// the hand switches to select mode and the snapshot becomes "hand_select" with
/// "select:&lt;i&gt;" and "confirm".
///
/// An enemy at 0 HP that the game keeps in the fight because it comes back is listed with
/// "reviving" and its intent, and is never a target (<see cref="Reviving"/>).
///
/// In co-op the state carries the "team" list (each teammate's HP, Block, powers, whether they
/// ended the turn and the damage headed for them) and a "co_op" line with the shared-turn
/// rules, and a card play is followed by the host's echo of it rather than by a timer, because
/// it can sit behind a teammate's plays for seconds.
/// </summary>
internal sealed class CombatReader : IScreen
{
    public static readonly CombatReader Instance = new();

    public string Name => "combat";

    private const string HandSelectScreen = "hand_select";

    private static readonly TimeSpan PlayTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan EndTurnTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SelectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PromptMaxAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EndTurnLost = TimeSpan.FromSeconds(12);

    /// <summary>When we last sent EndPlayerTurnAction; only read while the latch names the current turn.</summary>
    private static DateTime _endTurnRequestedAt;

    /// <summary>Every enemy of <see cref="_rosterCombat"/> in order of arrival; see <see cref="EnemyRoster"/>.</summary>
    private static readonly List<Creature> Roster = new();
    private static ICombatState? _rosterCombat;

    /// <summary>Everything one capture or execute reads, fetched once. <see cref="Coop"/> is false in a solo run.</summary>
    private sealed record Board(RunState Run, Player Me, ICombatState Cs, PlayerCombatState Pcs, List<Creature> Enemies, List<Player> Players, bool Coop);

    private sealed record PlayOption(string Id, string Label, CardModel Card, Creature? Target);

    private sealed record SelectOption(string Id, string Label, CardModel Card, NCardHolder Holder);

    private sealed record HandSelection(string Mode, string? Prompt, int? Min, int? Max, List<CardModel>? Selected, Func<CardModel, bool>? Filter)
    {
        public bool IsSelected(CardModel card) => Selected?.Any(c => ReferenceEquals(c, card)) == true;

        public bool Full => Max is { } max && Selected is { } s && s.Count >= max;
    }

    private CombatReader() { }

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (Enter(ctx, node, out var why) is not { } b)
            return GameSnapshot.Busy(Name, why ?? "combat loading");

        if (LiveHand() is { IsInCardSelection: true } selectHand)
            return CaptureHandSelect(ctx, b, selectHand);

        if (!MayAct(ctx, out why))
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = why, State = WaitingState(ctx, b) };

        var plays = new List<PlayOption>();
        var hand = new JsonArray();
        var cards = b.Pcs.Hand.Cards;
        var targets = Enumerable.Range(0, b.Enemies.Count)
            .Where(j => IsTargetableEnemy(b.Cs, b.Enemies[j]))
            .Select(j => ($"e{j}", b.Enemies[j]))
            .ToList();
        for (var i = 0; i < cards.Count; i++)
        {
            var options = PlayOptionsFor(b, i, cards[i], out var unplayable);
            plays.AddRange(options);

            var entry = Describe.Lead("i", i, Describe.HandCard(cards[i], targets));
            entry["playable"] = options.Count > 0;
            if (options.Count == 0 && unplayable is not null) entry["unplayable"] = unplayable;
            hand.Add(entry);
        }

        var actions = plays.Select(o => new LegalAction(o.Id, o.Label)).ToList();
        actions.AddRange(PotionActions.Legal(ctx, inCombat: true));
        actions.Add(new LegalAction("end_turn", "End turn"));

        return new GameSnapshot
        {
            Screen = Name,
            AwaitingDecision = true,
            State = BoardState(ctx, b, hand),
            Actions = actions,
        };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (Enter(ctx, node, out var why) is not { } b)
            return ActionResult.Fail($"No fight to act in ({why}).");

        if (LiveHand() is { IsInCardSelection: true } selectHand)
            return ExecuteHandSelect(ctx, b, selectHand, actionId);

        if (!MayAct(ctx, out why))
            return ActionResult.Fail($"Cannot act now ({why}).");

        if (PotionActions.TryExecute(ctx, actionId, inCombat: true, out var potionResult))
            return potionResult;

        if (actionId == "end_turn")
            return EndTurn(ctx, b);

        if (actionId.StartsWith("play:", StringComparison.Ordinal))
            return Play(b, actionId);

        return ActionResult.Fail($"Unknown combat action '{actionId}'.");
    }

    // ---------- the may-act gate ----------

    /// <summary>
    /// The full "may the local player act now" gate from docs/game-api.md section c, plus our
    /// own latches (a tracked request in flight, end turn already requested). TryManualPlay,
    /// EnqueueManualUse and RequestEnqueue check none of this themselves.
    /// </summary>
    internal static bool MayAct(GameContext ctx, out string? why)
    {
        why = Blocker(ctx);
        return why is null;
    }

    private static string? Blocker(GameContext ctx)
    {
        var cm = CombatManager.Instance;
        if (cm is null || !cm.IsInProgress) return "combat not running";
        if (cm.IsOverOrEnding) return "combat ending";
        if (ctx.Me is not { } me || ctx.Combat is not { } cs || ctx.MyCombat is not { } pcs) return "combat loading";
        if (!me.Creature.IsAlive) return ctx.IsMultiplayer ? "you are down; teammates fight on" : "you are dead";
        if (ActionTracker.Pending is { } pending) return $"resolving {pending}";
        if (cs.CurrentSide != CombatSide.Player) return "enemy turn";

        // PlayerActionsDisabled only flips once EndPlayerTurnAction executes, hence our latch.
        if (EndTurnLatched(ctx, me) || cm.IsPlayerReadyToEndTurn(me))
            return ctx.IsMultiplayer ? "waiting for teammates to end turn" : "ending turn";

        if (!cm.IsPartOfPlayerTurn(me)) return ctx.IsMultiplayer ? "a teammate is taking an extra turn" : "not your turn";
        if (pcs.Phase != PlayerTurnPhase.Play) return $"turn phase {pcs.Phase}";
        if (RunManager.Instance?.ActionQueueSynchronizer?.CombatState != ActionSynchronizerCombatState.PlayPhase) return "turn changing";
        if (cm.PlayerActionsDisabled) return "resolving";
        // Singleplayer pauses the fight while the map or a deck view is open.
        if (cm.IsPaused) return "combat paused";

        if (LiveHand() is not { } hand) return "hand not ready";
        if (hand.IsInCardSelection) return "choosing cards from hand";
        if (hand.CurrentMode != NPlayerHand.Mode.Play || hand.InCardPlay) return "card in play";
        if (hand.PeekButton?.IsPeeking == true) return "peeking at the board";
        if (NOverlayStack.Instance?.ScreenCount > 0) return "a screen is open";
        return null;
    }

    private static Board? Enter(GameContext ctx, Node? node, out string? why)
    {
        why = null;
        var cm = CombatManager.Instance;
        if (cm is null || !cm.IsInProgress) { why = "combat not running"; return null; }
        if (cm.IsOverOrEnding) { why = "combat ending"; return null; }
        if (ctx.Run is not { } run || ctx.Me is not { } me || ctx.Combat is not { } cs || ctx.MyCombat is not { } pcs)
        {
            why = "combat loading";
            return null;
        }

        // Section f.3: the combat room counts only in its live-fight mode (an event's fight
        // starts VisualOnly and switches over; a reloaded finished fight is FinishedCombat).
        if (node is NCombatRoom room && GodotObject.IsInstanceValid(room) && room.Mode != CombatRoomMode.ActiveCombat)
        {
            why = $"combat room in {room.Mode} mode";
            return null;
        }

        return new Board(run, me, cs, pcs, EnemyRoster(cs), cs.Players.ToList(), Teammates.Active(ctx));
    }

    // ---------- playing cards ----------

    private static List<PlayOption> PlayOptionsFor(Board b, int i, CardModel card, out string? unplayable)
    {
        var options = new List<PlayOption>();
        unplayable = null;
        try
        {
            if (!card.CanPlay(out var reason, out _))
            {
                unplayable = Unplayable(reason);
                return options;
            }

            var name = Describe.CardName(card);
            switch (card.TargetType)
            {
                case TargetType.AnyEnemy:
                    for (var j = 0; j < b.Enemies.Count; j++)
                    {
                        var enemy = b.Enemies[j];
                        if (IsTargetableEnemy(b.Cs, enemy) && card.CanPlayTargeting(enemy))
                            options.Add(new PlayOption($"play:{i}>e{j}", $"Play {name} on {EnemyLabel(b.Enemies, j)}", card, enemy));
                    }
                    break;

                case TargetType.AnyAlly:
                    // Other living players only, as the UI offers: pets like Osty are not player creatures.
                    for (var j = 0; j < b.Players.Count; j++)
                    {
                        var ally = b.Players[j];
                        if (!LocalContext.IsMe(ally) && ally.Creature.IsAlive && Hook.ShouldAllowTargeting(b.Cs, ally.Creature, out _)
                            && card.CanPlayTargeting(ally.Creature))
                            options.Add(new PlayOption($"play:{i}>p{j}", $"Play {name} on {PlayerLabel(b.Players, j)}", card, ally.Creature));
                    }
                    break;

                default:
                    // Self, AllEnemies, RandomEnemy, AllAllies, AnyPlayer, Osty, None: the game picks.
                    if (card.CanPlayTargeting(null))
                        options.Add(new PlayOption($"play:{i}", $"Play {name}", card, null));
                    break;
            }

            if (options.Count == 0) unplayable = "no valid target";
        }
        catch (Exception ex)
        {
            options.Clear();
            unplayable = $"unreadable ({ex.GetType().Name})";
        }
        return options;
    }

    private static string Unplayable(UnplayableReason reason)
    {
        if ((reason & UnplayableReason.EnergyCostTooHigh) != 0) return "not enough energy";
        if ((reason & UnplayableReason.StarCostTooHigh) != 0) return "not enough stars";
        return reason.ToString();
    }

    private static ActionResult Play(Board b, string actionId)
    {
        PlayOption? option = null;
        var cards = b.Pcs.Hand.Cards;
        for (var i = 0; i < cards.Count && option is null; i++)
            option = PlayOptionsFor(b, i, cards[i], out _).FirstOrDefault(o => o.Id == actionId);
        if (option is null)
            return ActionResult.Fail($"'{actionId}' is not playable any more.");

        var card = option.Card;
        var name = Describe.CardName(card);
        var overlayBefore = Nodes.TopOverlay();
        var pcs = b.Pcs;
        var turn = pcs.TurnNumber;
        var energyBefore = pcs.Energy;
        var starsBefore = pcs.Stars;

        // Co-op: watch for the host's echo of this play (see TrackCoopPlay).
        var echoBefore = b.Coop ? WatchOwnPlays(b.Me) : null;

        if (!card.TryManualPlay(option.Target))
            return ActionResult.Fail($"The game refused to play {name}.");

        if (b.Coop && _ownPlaysWatched)
        {
            TrackCoopPlay(name, echoBefore, overlayBefore, turn);
            return ActionResult.Success;
        }

        // CanPlay keeps counting a queued card as in hand with its energy unspent, so stay busy
        // until the play has run (the card left the hand, or its cost was paid, which also
        // covers a card that comes straight back) and the queue has drained. That keeps a second
        // play from being offered off stale numbers. A play cancelled before it ran leaves
        // everything as it was; the turn moving on or the timeout ends that wait.
        var played = false;
        ActionTracker.Begin(name, c =>
        {
            if (FightOver() || HandSelectOpen() || !ReferenceEquals(Nodes.TopOverlay(), overlayBefore)) return true;
            if (c.MyCombat?.TurnNumber != turn || c.Combat?.CurrentSide != CombatSide.Player) return true;
            played |= card.Pile?.Type != PileType.Hand || pcs.Energy != energyBefore || pcs.Stars != starsBefore;
            return played && ActionTracker.GameLooksIdle();
        }, PlayTimeout);
        return ActionResult.Success;
    }

    // ---------- co-op: following a play by its echo ----------

    /// <summary>A play the host has not ordered after this long is taken to be lost, and the card is offered again.</summary>
    private static readonly TimeSpan EchoLost = TimeSpan.FromSeconds(10);

    /// <summary>How long a play may wait behind teammates' actions before the bot stops holding its snapshots for it.</summary>
    private static readonly TimeSpan QueuedPlayTimeout = TimeSpan.FromSeconds(90);

    private static ActionQueueSet? _watchedQueues;
    private static ulong _watchedNetId;
    private static bool _ownPlaysWatched;
    private static bool _ownPlaysFailed;

    /// <summary>The newest card play of ours that the host has ordered, as the action object that really runs.</summary>
    private static PlayCardAction? _ownPlay;

    /// <summary>
    /// Subscribes (once per run's queue set) to the game's "an action was ordered" event and
    /// returns the last own play seen so far. One action runs at a time for the whole table,
    /// so in co-op our card can stay in hand, with its energy unspent, for as long as a
    /// teammate's earlier plays take; a timer would offer it again and queue it twice. The
    /// echo says where it really is (votes.md 2.1). Read-only and the same on host and client;
    /// on the host the echo arrives inside TryManualPlay.
    /// </summary>
    private static PlayCardAction? WatchOwnPlays(Player me)
    {
        if (_ownPlaysFailed) return null;
        try
        {
            var queues = RunManager.Instance?.ActionQueueSet;
            if (!ReferenceEquals(queues, _watchedQueues))
            {
                if (_watchedQueues is { } old) old.ActionEnqueued -= OnActionEnqueued;
                _watchedQueues = queues;
                _ownPlay = null;
                _ownPlaysWatched = queues is not null;
                if (queues is not null) queues.ActionEnqueued += OnActionEnqueued;
            }
            _watchedNetId = me.NetId;
        }
        catch (Exception ex)
        {
            // Without the echo the solo tracking below still works; it is only less patient.
            _ownPlaysFailed = true;
            _ownPlaysWatched = false;
            ModEntry.LogError($"coop: can't watch the action queue, so card plays are tracked by timer: {ex.Message}");
        }
        return _ownPlay;
    }

    // Raised by the game in the middle of ordering an action: it must never throw.
    private static void OnActionEnqueued(GameAction action)
    {
        try
        {
            if (action is PlayCardAction play && play.OwnerId == _watchedNetId) _ownPlay = play;
        }
        catch
        {
            // Nothing to do; the tracker falls back to its timeout.
        }
    }

    /// <summary>
    /// Holds snapshots until our play has run or been cancelled, as the echoed action itself
    /// reports. Finished or cancelled, the board is ours to read again even while a teammate's
    /// action is still animating.
    /// </summary>
    private static void TrackCoopPlay(string name, PlayCardAction? echoBefore, object? overlayBefore, int turn)
    {
        var since = DateTime.UtcNow;
        var lostLogged = false;
        ActionTracker.Begin(name, c =>
        {
            if (FightOver() || HandSelectOpen() || !ReferenceEquals(Nodes.TopOverlay(), overlayBefore)) return true;
            if (c.MyCombat?.TurnNumber != turn || c.Combat?.CurrentSide != CombatSide.Player) return true;

            var echo = _ownPlay;
            if (echo is null || ReferenceEquals(echo, echoBefore))
            {
                // Not ordered by the host yet.
                if (DateTime.UtcNow - since < EchoLost) return false;
                if (!lostLogged)
                {
                    lostLogged = true;
                    ModEntry.Log($"coop: the host did not order the play of {name} within {EchoLost.TotalSeconds:0} s; offering the hand again");
                }
                return true;
            }
            return echo.State is GameActionState.Finished or GameActionState.Canceled;
        }, QueuedPlayTimeout);
    }

    /// <summary>
    /// Our end-turn latch for this turn. A request the game or host drops (BeforeCancelled)
    /// would otherwise hold it forever, since the turn never ends: once it is old, we are
    /// still not marked ready, actions are still enabled and the queue is idle, stop honouring
    /// it so end_turn is offered again. A repeat carries the same turn number.
    /// </summary>
    private static bool EndTurnLatched(GameContext ctx, Player me)
    {
        if (!Latches.EndTurnAlreadyRequested(ctx)) return false;
        if (DateTime.UtcNow - _endTurnRequestedAt < EndTurnLost) return true;
        return CombatManager.Instance is not { } cm
            || cm.PlayerActionsDisabled || cm.IsPlayerReadyToEndTurn(me) || !ActionTracker.GameLooksIdle();
    }

    private static ActionResult EndTurn(GameContext ctx, Board b)
    {
        if (EndTurnLatched(ctx, b.Me))
            return ActionResult.Fail("This turn is already ended.");
        if (RunManager.Instance?.ActionQueueSynchronizer is not { } sync)
            return ActionResult.Fail("The action queue is not ready.");

        var me = b.Me;
        var turn = b.Pcs.TurnNumber;

        // Latch first so it names this turn even if the action runs inside RequestEnqueue.
        Latches.MarkEndTurn(ctx);
        _endTurnRequestedAt = DateTime.UtcNow;
        try
        {
            sync.RequestEnqueue(new EndPlayerTurnAction(me, turn));
        }
        catch
        {
            Latches.EndTurnRequestedFor = -1;
            throw;
        }

        ActionTracker.Begin("end turn", c =>
        {
            // End-of-turn effects (retain, discard choices) open a selection: that is the next decision.
            if (FightOver() || HandSelectOpen() || Nodes.TopOverlay() is not null) return true;
            if (CombatManager.Instance is { } cm && (cm.PlayerActionsDisabled || cm.IsPlayerReadyToEndTurn(me))) return true;
            return c.MyCombat?.TurnNumber != turn || c.Combat?.CurrentSide != CombatSide.Player;
        }, EndTurnTimeout);
        return ActionResult.Success;
    }

    // ---------- hand select mode ----------

    private static GameSnapshot CaptureHandSelect(GameContext ctx, Board b, NPlayerHand selectHand)
    {
        var sel = ReadSelection(selectHand);
        var options = Choosable(b, selectHand, sel);
        var blocker = HandSelectBlocker(ctx);
        var canConfirm = ConfirmButton(selectHand) is { IsEnabled: true };

        var hand = new JsonArray();
        var cards = b.Pcs.Hand.Cards;
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var entry = Describe.Lead("i", i, Describe.Card(card, PileType.Hand));
            if (sel.IsSelected(card)) entry["selected"] = true;
            else entry["selectable"] = options.Any(o => ReferenceEquals(o.Card, card));
            hand.Add(entry);
        }

        var state = BoardState(ctx, b, hand);
        state["selection"] = SelectionJson(sel);

        var actions = options.Select(o => new LegalAction(o.Id, o.Label)).ToList();
        if (canConfirm)
            actions.Add(new LegalAction("confirm", sel.Selected is { } s ? $"Confirm selection ({s.Count} chosen)" : "Confirm selection"));

        var awaiting = blocker is null && actions.Count > 0;
        return new GameSnapshot
        {
            Screen = HandSelectScreen,
            AwaitingDecision = awaiting,
            Waiting = awaiting ? null : blocker ?? "selection not ready",
            State = state,
            Actions = awaiting ? actions : new List<LegalAction>(),
        };
    }

    private static ActionResult ExecuteHandSelect(GameContext ctx, Board b, NPlayerHand selectHand, string actionId)
    {
        if (HandSelectBlocker(ctx) is { } why)
            return ActionResult.Fail($"Cannot choose now ({why}).");

        var overlayBefore = Nodes.TopOverlay();
        // A follow-up hand choice can reopen select mode between polls, so "select mode closed"
        // is never seen; a fresh CardSelectCmd.From* record says the next choice is up.
        var promptBefore = ChoicePrompt.Latest;

        if (actionId == "confirm")
        {
            if (ConfirmButton(selectHand) is not { } button || !button.IsEnabled)
                return ActionResult.Fail("The confirm button is not enabled yet.");

            var confirmed = Clicks.Once(button, () => button.ForceClick(), "the confirm button");
            if (confirmed.Ok)
            {
                ActionTracker.Begin("the selection",
                    _ => FightOver() || !ReferenceEquals(Nodes.TopOverlay(), overlayBefore) || !ReferenceEquals(ChoicePrompt.Latest, promptBefore)
                         || (!HandSelectOpen() && ActionTracker.GameLooksIdle()),
                    PlayTimeout);
            }
            return confirmed;
        }

        if (!actionId.StartsWith("select:", StringComparison.Ordinal))
            return ActionResult.Fail($"'{actionId}' is not a hand selection action.");

        // Holders move while the hand re-lays itself out, so find the holder fresh each time.
        var sel = ReadSelection(selectHand);
        if (Choosable(b, selectHand, sel).FirstOrDefault(o => o.Id == actionId) is not { } option)
            return ActionResult.Fail($"'{actionId}' cannot be selected now.");

        var holder = option.Holder;
        var name = Describe.CardName(option.Card);
        var before = SelectionSignature(selectHand);
        var picked = Clicks.Once(holder, () => holder.EmitSignal(NCardHolder.SignalName.Pressed, holder), name);
        if (picked.Ok)
        {
            ActionTracker.Begin($"selecting {name}", _ =>
            {
                if (FightOver() || !ReferenceEquals(Nodes.TopOverlay(), overlayBefore) || !ReferenceEquals(ChoicePrompt.Latest, promptBefore)) return true;
                // Reaching the maximum can close select mode by itself and resume the card.
                return HandSelectOpen() ? SelectionSignature(selectHand) != before : ActionTracker.GameLooksIdle();
            }, SelectTimeout, TimeSpan.FromMilliseconds(200));
        }
        return picked;
    }

    /// <summary>Select mode has its own gate: it can open at end of turn or mid-card, when the normal gate is closed.</summary>
    private static string? HandSelectBlocker(GameContext ctx)
    {
        if (ctx.Me?.Creature.IsAlive != true) return "you are dead";
        if (ActionTracker.Pending is { } pending) return $"resolving {pending}";
        if (NOverlayStack.Instance?.ScreenCount > 0) return "a screen is open";
        // Presses only wiggle the peek button, and confirm stays disabled, while peeking.
        if (LiveHand()?.PeekButton?.IsPeeking == true) return "peeking at the board";
        return null;
    }

    private static HandSelection ReadSelection(NPlayerHand hand)
    {
        var mode = hand.CurrentMode == NPlayerHand.Mode.UpgradeSelect ? "upgrade" : "select";
        // The hand's private _prefs belong to this very selection and never age out, so a slow
        // decision keeps its prompt and limits; the CardSelectCmd capture is the fallback.
        if (Nodes.TryGetPrivate(hand, "_prefs") is CardSelectorPrefs { Prompt: not null } prefs)
            return new HandSelection(mode, Describe.Text(prefs.Prompt), prefs.MinSelect, prefs.MaxSelect, SelectedCards(hand), SelectionFilter(hand));

        // Only a hand factory's prompt can describe this; an older grid prompt would mislead.
        var prompt = ChoicePrompt.Recent(PromptMaxAge) is { } p && p.Source.StartsWith("FromHand", StringComparison.Ordinal) ? p : null;
        return new HandSelection(mode, prompt?.Prompt, prompt?.Min, prompt?.Max, SelectedCards(hand), SelectionFilter(hand));
    }

    /// <summary>
    /// Cards we can press now. The filter and picked-so-far list come from the hand's private
    /// state; if they rule out everything while confirm is still disabled, something is off,
    /// so offer every shown card rather than stall (the hand hides cards the filter rejects).
    /// </summary>
    private static List<SelectOption> Choosable(Board b, NPlayerHand hand, HandSelection sel)
    {
        var options = SelectOptions(b, hand, sel, strict: true);
        if (options.Count == 0 && ConfirmButton(hand) is not { IsEnabled: true })
            options = SelectOptions(b, hand, sel, strict: false);
        return options;
    }

    private static List<SelectOption> SelectOptions(Board b, NPlayerHand hand, HandSelection sel, bool strict)
    {
        var options = new List<SelectOption>();
        if (strict && sel.Full) return options;

        // Visible holders only: picked cards move to another container and filtered ones are hidden.
        var holders = hand.ActiveHolders.ToList();
        var cards = b.Pcs.Hand.Cards;
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (strict && sel.IsSelected(card)) continue;
            if (strict && sel.Filter is { } filter && !Passes(filter, card)) continue;

            NCardHolder? holder = holders.FirstOrDefault(h => GodotObject.IsInstanceValid(h) && ReferenceEquals(h.CardModel, card));
            if (holder is null) continue;
            options.Add(new SelectOption($"select:{i}", $"Select {Describe.CardName(card)}", card, holder));
        }
        return options;
    }

    private static JsonObject SelectionJson(HandSelection sel)
    {
        var json = new JsonObject { ["mode"] = sel.Mode };
        if (sel.Prompt is { Length: > 0 } prompt) json["prompt"] = prompt;
        if (sel.Min is { } min) json["min"] = min;
        if (sel.Max is { } max) json["max"] = max;
        if (sel.Selected is { } selected)
        {
            var names = new JsonArray();
            foreach (var card in selected) names.Add(Describe.CardName(card));
            json["selected"] = names;
        }
        return json;
    }

    private static NConfirmButton? ConfirmButton(NPlayerHand hand) =>
        Nodes.Get<NConfirmButton>(hand, "%SelectModeConfirmButton");

    /// <summary>Something that changes when a press registers, for the select tracker.</summary>
    private static string SelectionSignature(NPlayerHand hand)
    {
        if (!GodotObject.IsInstanceValid(hand)) return "gone";
        var selected = SelectedCards(hand)?.Count ?? -1;
        var holders = hand.ActiveHolders.Count();
        var confirm = ConfirmButton(hand)?.IsEnabled == true;
        return $"{hand.CurrentMode}/{selected}/{holders}/{confirm}";
    }

    private static bool Passes(Func<CardModel, bool> filter, CardModel card)
    {
        try { return filter(card); }
        catch { return true; }
    }

    // The hand doesn't publish what is picked so far or which cards qualify; read its private
    // state when it can be found and fall back to "unknown" (everything offered) otherwise.
    // Both are private fields of NPlayerHand, set in SelectCards and cleared when it ends.

    private static List<CardModel>? SelectedCards(NPlayerHand hand) =>
        Nodes.TryGetPrivate(hand, "_selectedCards") is IEnumerable<CardModel> cards ? cards.ToList() : null;

    private static Func<CardModel, bool>? SelectionFilter(NPlayerHand hand) =>
        Nodes.TryGetPrivate(hand, "_currentSelectionFilter") as Func<CardModel, bool>;

    // ---------- state ----------

    private static JsonObject BoardState(GameContext ctx, Board b, JsonArray hand)
    {
        var pcs = b.Pcs;

        var me = Describe.Creature(b.Me.Creature);
        AddOrbs(me, pcs);
        var team = b.Coop ? CoopTeam(ctx, b) : null;

        var run = Describe.RunInfo(b.Run);
        if (b.Run.CurrentRoom is { } room) run["room"] = room.RoomType.ToString();

        var state = new JsonObject
        {
            ["run"] = run,
            ["turn"] = pcs.TurnNumber,
            ["round"] = b.Cs.RoundNumber,
            ["energy"] = pcs.Energy,
            ["max_energy"] = pcs.MaxEnergy,
        };
        if (UsesStars(b)) state["stars"] = pcs.Stars;
        state["me"] = me;
        if (Pets(pcs) is { } pets) state["pets"] = pets;
        if (team is not null)
        {
            // Our own player target id, next to the teammates' in "team".
            state["you"] = $"p{b.Players.IndexOf(b.Me)}";
            state["team"] = team;
            state["co_op"] = CoopManners.CombatNote;
        }
        else if (b.Players.Count > 1) state["teammates"] = TeammateList(b);
        state["enemies"] = Enemies(b);
        if (IncomingDamage(b) is { } incoming) state["incoming_damage"] = incoming;
        state["hand"] = hand;
        // Counts by name only: draw order is hidden from human players too.
        state["draw_pile"] = Pile(pcs.DrawPile);
        state["discard_pile"] = Pile(pcs.DiscardPile);
        state["exhaust_count"] = pcs.ExhaustPile.Cards.Count;
        state["potions"] = PotionActions.Describe(ctx, inCombat: true);

        var relics = new JsonArray();
        foreach (var relic in b.Me.Relics) relics.Add(Describe.Text(relic.Title));
        state["relics"] = relics;
        return state;
    }

    /// <summary>Small state for snapshots where we are only waiting (enemy turn, teammates).</summary>
    private static JsonObject WaitingState(GameContext ctx, Board b)
    {
        var json = new JsonObject
        {
            ["turn"] = b.Pcs.TurnNumber,
            ["round"] = b.Cs.RoundNumber,
            ["hp"] = b.Me.Creature.CurrentHp,
            ["block"] = b.Me.Creature.Block,
        };
        if (b.Coop && Teammates.Team(ctx) is { } team)
        {
            json["team"] = team;
        }
        else if (b.Players.Count > 1)
        {
            var mates = new JsonArray();
            for (var j = 0; j < b.Players.Count; j++)
            {
                var p = b.Players[j];
                if (LocalContext.IsMe(p)) continue;
                mates.Add(new JsonObject { ["i"] = j, ["name"] = PlayerName(p), ["ended_turn"] = EndedTurn(p) });
            }
            json["teammates"] = mates;
        }
        return json;
    }

    private static JsonArray Enemies(Board b) => EnemiesJson(b.Cs, b.Enemies);

    /// <summary>
    /// The enemies of <paramref name="roster"/> (from <see cref="EnemyRoster"/>) still in the
    /// fight, each with its roster index as "i" (the j of e&lt;j&gt;) and its intents. Shared with
    /// the mid-combat card choice screens so they name enemies as the combat snapshots do.
    /// </summary>
    internal static JsonArray EnemiesJson(ICombatState cs, IReadOnlyList<Creature> roster)
    {
        var arr = new JsonArray();
        var living = roster.Where(e => InFight(cs, e)).ToList();
        // "primary" only says something when some living enemies are and some aren't.
        var mixed = living.Any(e => e.IsPrimaryEnemy) && living.Any(e => !e.IsPrimaryEnemy);

        for (var j = 0; j < roster.Count; j++)
        {
            var enemy = roster[j];
            var reviving = Reviving(cs, enemy);
            if (!reviving && !InFight(cs, enemy)) continue;
            var json = Describe.Lead("i", j, Describe.Creature(enemy));
            // Down at 0 HP but kept in the fight: it comes back, and its intent says when and how.
            if (reviving) json["reviving"] = true;
            json["intents"] = Intents(enemy, cs);
            if (mixed && enemy.IsPrimaryEnemy) json["primary"] = true;
            if (!enemy.IsHittable) json["untargetable"] = true;
            arr.Add(json);
        }
        return arr;
    }

    private static JsonArray Intents(Creature enemy, ICombatState cs)
    {
        try
        {
            return Describe.Intents(enemy, cs);
        }
        catch
        {
            var unknown = new JsonArray();
            unknown.Add("unknown");
            return unknown;
        }
    }

    private static int? IncomingDamage(Board b) => IncomingDamage(b.Cs, b.Enemies);

    /// <summary>Sum of attack intents as the game computes them against the local player.</summary>
    internal static int? IncomingDamage(ICombatState cs, IReadOnlyList<Creature> roster)
    {
        try
        {
            var targets = cs.Players.Select(p => p.Creature).ToList();
            var total = 0;
            foreach (var enemy in roster)
            {
                if (!Acting(cs, enemy) || enemy.Monster?.NextMove is not { } move || move.Id == "STUNNED") continue;
                foreach (var intent in move.Intents)
                {
                    // "is AttackIntent", not IntentType.Attack: death blows report their own type.
                    if (intent is AttackIntent attack) total += attack.GetTotalDamage(targets, enemy);
                }
            }
            return total;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The co-op "team" list for a fight: the shared entry per teammate plus what only the
    /// fight knows, their powers and the damage the enemies' intents deal to them. Null when
    /// the read failed (the plain teammates list is shown then).
    /// </summary>
    private static JsonArray? CoopTeam(GameContext ctx, Board b) =>
        Teammates.Safe<JsonArray?>("the team in a fight", null, () =>
        {
            var team = new JsonArray();
            foreach (var mate in Teammates.Others(ctx))
            {
                var json = Teammates.Json(mate, ctx);
                // The id that card and potion targets use: the player's place in this fight
                // (the same as the run's slot, since the fight seats players in run order).
                if (b.Players.IndexOf(mate.Player) is >= 0 and var seat) json["id"] = $"p{seat}";
                var powers = Describe.Powers(mate.Player.Creature);
                if (powers.Count > 0) json["powers"] = powers;
                if (mate.Player.Creature.IsAlive && IncomingDamageFor(b, mate.Player) is { } incoming)
                    json["incoming"] = incoming;
                team.Add(json);
            }
            return team;
        });

    /// <summary>
    /// Sum of the enemies' attack intents against one player, before Block. Every monster
    /// attack hits every living player, but the number on an intent is worked out for the local
    /// player only (AttackIntent.GetSingleDamage ignores its targets), so a teammate's is the
    /// same formula with them as the target: their Vulnerable, relics and cover count, ours
    /// don't (votes.md 2.5). A pure calculation, the same on host and client.
    /// </summary>
    private static int? IncomingDamageFor(Board b, Player p)
    {
        try
        {
            var total = 0;
            foreach (var enemy in b.Enemies)
            {
                if (!Acting(b.Cs, enemy) || enemy.Monster?.NextMove is not { } move || move.Id == "STUNNED") continue;
                foreach (var intent in move.Intents)
                {
                    if (intent is not AttackIntent { DamageCalc: { } damage } attack) continue;
                    var perHit = Hook.ModifyDamage(p.RunState, p.Creature.CombatState, p.Creature, enemy, damage(),
                        ValueProp.Move, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
                    // As the game totals it: one hit for a single attack (a death blow is one), the
                    // hit count for a multi-attack, which can be 0 when it is worked out from the board.
                    var hits = attack is MultiAttackIntent ? Math.Max(0, attack.Repeats) : Math.Max(1, attack.Repeats);
                    total += Math.Max(0, (int)perHit) * hits;
                }
            }
            return total;
        }
        catch
        {
            return null;
        }
    }

    private static JsonArray TeammateList(Board b)
    {
        var arr = new JsonArray();
        for (var j = 0; j < b.Players.Count; j++)
        {
            var p = b.Players[j];
            if (LocalContext.IsMe(p)) continue;
            var json = Describe.Lead("i", j, Describe.Creature(p.Creature));
            json["ended_turn"] = EndedTurn(p);
            arr.Add(json);
        }
        return arr;
    }

    private static bool EndedTurn(Player p)
    {
        try { return CombatManager.Instance?.IsPlayerReadyToEndTurn(p) == true; }
        catch { return false; }
    }

    private static void AddOrbs(JsonObject me, PlayerCombatState pcs)
    {
        try
        {
            var queue = pcs.OrbQueue;
            if (queue is null || (queue.Capacity <= 0 && !queue.Orbs.Any())) return;

            var orbs = new JsonArray();
            foreach (var orb in queue.Orbs)
            {
                // PassiveVal and EvokeVal are decimals with Focus already applied.
                orbs.Add(new JsonObject
                {
                    ["id"] = orb.Id.Entry,
                    ["passive"] = (int)orb.PassiveVal,
                    ["evoke"] = (int)orb.EvokeVal,
                });
            }
            me["orb_slots"] = queue.Capacity;
            me["orbs"] = orbs; // [0] is evoked next
        }
        catch
        {
            // Orbs are a bonus; never lose the whole snapshot over them.
        }
    }

    private static JsonArray? Pets(PlayerCombatState pcs)
    {
        try
        {
            var arr = new JsonArray();
            // Necrobinder's Osty.
            foreach (var pet in pcs.Pets) arr.Add(Describe.Creature(pet));
            return arr.Count > 0 ? arr : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool UsesStars(Board b) => b.Pcs.Stars != 0 || b.Me.Character is Regent;

    private static JsonObject Pile(CardPile pile) => new()
    {
        ["count"] = pile.Cards.Count,
        ["cards"] = Describe.DeckCounts(pile.Cards),
    };

    // ---------- helpers shared with PotionActions ----------

    internal static NPlayerHand? LiveHand() =>
        NPlayerHand.Instance is { } hand && GodotObject.IsInstanceValid(hand) ? hand : null;

    internal static bool HandSelectOpen() => LiveHand()?.IsInCardSelection == true;

    internal static bool FightOver() =>
        CombatManager.Instance is not { IsInProgress: true, IsOverOrEnding: false };

    /// <summary>
    /// The fight's enemies for e&lt;j&gt; ids, each at the index it first got. The game drops dead
    /// and fled enemies from cs.Enemies, which would shift later ids while the model decides
    /// (a teammate's kill in co-op); here they stay as gaps that <see cref="InFight"/> skips.
    /// </summary>
    internal static List<Creature> EnemyRoster(ICombatState cs)
    {
        if (!ReferenceEquals(_rosterCombat, cs))
        {
            _rosterCombat = cs;
            Roster.Clear();
        }
        foreach (var enemy in cs.Enemies)
            if (!Roster.Contains(enemy)) Roster.Add(enemy);
        return Roster.ToList();
    }

    /// <summary>A living enemy still in the fight (fled ones are alive but gone).</summary>
    internal static bool InFight(ICombatState cs, Creature enemy) => enemy.IsAlive && cs.Enemies.Contains(enemy);

    /// <summary>
    /// An enemy at 0 HP that the game keeps in the fight because it comes back: a power of
    /// its own refuses its removal (a Decimillipede segment that reattaches, Test Subject
    /// between its forms, the illusions Eye With Teeth and Parafright). It keeps its place in
    /// the enemy list and takes its turn, which is the move that heals it, so the snapshot
    /// shows it with that intent. The game refuses to target it (a card and a potion both need
    /// a living target), and <see cref="IsTargetableEnemy"/> keeps requiring a living one.
    /// The hook is the question CreatureCmd asks at the death itself; it only reads.
    /// </summary>
    internal static bool Reviving(ICombatState cs, Creature enemy)
    {
        try
        {
            return enemy.IsDead && cs.Enemies.Contains(enemy) && !Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, enemy);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>An enemy whose next move will be performed: a living one, or one that is down but coming back.</summary>
    private static bool Acting(ICombatState cs, Creature enemy) => InFight(cs, enemy) || Reviving(cs, enemy);

    /// <summary>Docs section c: a living, hittable enemy the targeting hooks allow.</summary>
    internal static bool IsTargetableEnemy(ICombatState cs, Creature enemy) =>
        InFight(cs, enemy) && enemy.IsHittable
        && Hook.ShouldAllowTargeting(cs, enemy, out _);

    /// <summary>Enemy name for labels, with its id when another living enemy shares the name.</summary>
    internal static string EnemyLabel(IReadOnlyList<Creature> enemies, int j)
    {
        var name = Describe.Text(enemies[j].Name);
        var twins = enemies.Count(e => e.IsAlive && Describe.Text(e.Name) == name);
        return twins > 1 ? $"{name} (e{j})" : name;
    }

    /// <summary>Teammate name for labels, with its id when another player shares the name.</summary>
    internal static string PlayerLabel(IReadOnlyList<Player> players, int j)
    {
        var name = PlayerName(players[j]);
        var twins = players.Count(p => PlayerName(p) == name);
        return twins > 1 ? $"{name} (p{j})" : name;
    }

    private static string PlayerName(Player p)
    {
        var name = Describe.Text(p.Creature.Name);
        return name.Length > 0 ? name : Describe.Text(p.Character?.Title);
    }
}
