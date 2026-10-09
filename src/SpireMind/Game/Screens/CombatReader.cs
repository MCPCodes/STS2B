using System.Reflection;
using System.Text.Json.Nodes;
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

    /// <summary>Everything one capture or execute reads, fetched once.</summary>
    private sealed record Board(RunState Run, Player Me, ICombatState Cs, PlayerCombatState Pcs, List<Creature> Enemies, List<Player> Players);

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
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = why, State = WaitingState(b) };

        var plays = new List<PlayOption>();
        var hand = new JsonArray();
        var cards = b.Pcs.Hand.Cards;
        for (var i = 0; i < cards.Count; i++)
        {
            var options = PlayOptionsFor(b, i, cards[i], out var unplayable);
            plays.AddRange(options);

            var entry = Describe.Lead("i", i, Describe.Card(cards[i], PileType.Hand));
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

        // Section f.3: the combat room counts only in its live-fight mode. Compared by name so
        // the enum's type name doesn't matter.
        // VERIFY: NCombatRoom.Mode exists and its live-fight value is named ActiveCombat.
        if (node is NCombatRoom room && GodotObject.IsInstanceValid(room) && room.Mode.ToString() != "ActiveCombat")
        {
            why = $"combat room in {room.Mode} mode";
            return null;
        }

        return new Board(run, me, cs, pcs, cs.Enemies.ToList(), cs.Players.ToList());
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
            // VERIFY: the enum behind CardModel.TargetType is named TargetType.
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
                    // VERIFY: section c says "a living PlayerCreature other than me"; if that includes pets (Osty), they are not offered here.
                    for (var j = 0; j < b.Players.Count; j++)
                    {
                        var ally = b.Players[j];
                        if (!LocalContext.IsMe(ally) && ally.Creature.IsAlive && card.CanPlayTargeting(ally.Creature))
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

        if (!card.TryManualPlay(option.Target))
            return ActionResult.Fail($"The game refused to play {name}.");

        // CanPlay keeps counting a queued card as in hand with its energy unspent, so stay busy
        // until the card has left the hand and the queue has drained. That keeps a second play
        // from being offered off stale numbers.
        var leftHand = false;
        ActionTracker.Begin(name, _ =>
        {
            if (FightOver() || HandSelectOpen() || !ReferenceEquals(Nodes.TopOverlay(), overlayBefore)) return true;
            leftHand |= card.Pile?.Type != PileType.Hand;
            return leftHand && ActionTracker.GameLooksIdle();
        }, PlayTimeout);
        return ActionResult.Success;
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
        return null;
    }

    private static HandSelection ReadSelection(NPlayerHand hand)
    {
        // Only a hand factory's prompt can describe this; an older grid prompt would mislead.
        var prompt = ChoicePrompt.Recent(PromptMaxAge) is { } p && p.Source.StartsWith("FromHand", StringComparison.Ordinal) ? p : null;
        var mode = hand.CurrentMode == NPlayerHand.Mode.UpgradeSelect ? "upgrade" : "select";
        return new HandSelection(mode, prompt?.Prompt, prompt?.Min, prompt?.Max, SelectedCards(hand), SelectionFilter(hand));
    }

    /// <summary>
    /// Cards we can press now. The filter and picked-so-far list come from guessed private
    /// members; if they rule out everything while confirm is still disabled, they are probably
    /// wrong, so offer every card rather than stall (pressing an ineligible card does nothing).
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

        // VERIFY: NPlayerHand.ActiveHolders elements derive from NCardHolder and expose CardModel.
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

    // VERIFY: name of NPlayerHand's member holding the cards picked so far in select mode.
    private static readonly string[] SelectedMembers = ["_selectedCards", "SelectedCards", "_selectedHolders", "_selectedCardHolders"];

    private static FieldInfo? _filterField;
    private static bool _filterSearched;

    private static List<CardModel>? SelectedCards(NPlayerHand hand)
    {
        foreach (var member in SelectedMembers)
        {
            switch (Nodes.GetPrivate(hand, member))
            {
                case IEnumerable<CardModel> cards:
                    return cards.ToList();
                case IEnumerable<NCardHolder> holders:
                    return holders.Where(h => GodotObject.IsInstanceValid(h)).Select(h => h.CardModel).OfType<CardModel>().ToList();
            }
        }
        return null;
    }

    // VERIFY: NPlayerHand keeps the CardSelectCmd filter as a Func<CardModel, bool> (or Predicate) field in select mode.
    private static Func<CardModel, bool>? SelectionFilter(NPlayerHand hand)
    {
        if (!_filterSearched)
        {
            _filterSearched = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = hand.GetType(); t is not null && _filterField is null; t = t.BaseType)
                _filterField = t.GetFields(flags).FirstOrDefault(f => f.FieldType == typeof(Func<CardModel, bool>) || f.FieldType == typeof(Predicate<CardModel>));
        }

        return _filterField?.GetValue(hand) switch
        {
            Func<CardModel, bool> f => f,
            Predicate<CardModel> p => new Func<CardModel, bool>(p),
            _ => null,
        };
    }

    // ---------- state ----------

    private static JsonObject BoardState(GameContext ctx, Board b, JsonArray hand)
    {
        var pcs = b.Pcs;

        var me = Describe.Creature(b.Me.Creature);
        AddOrbs(me, pcs);

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
        if (b.Players.Count > 1) state["teammates"] = Teammates(b);
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
    private static JsonObject WaitingState(Board b)
    {
        var json = new JsonObject
        {
            ["turn"] = b.Pcs.TurnNumber,
            ["round"] = b.Cs.RoundNumber,
            ["hp"] = b.Me.Creature.CurrentHp,
            ["block"] = b.Me.Creature.Block,
        };
        if (b.Players.Count > 1)
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

    private static JsonArray Enemies(Board b)
    {
        var arr = new JsonArray();
        var living = b.Enemies.Where(e => e.IsAlive).ToList();
        // "primary" only says something when some living enemies are and some aren't.
        var mixed = living.Any(e => e.IsPrimaryEnemy) && living.Any(e => !e.IsPrimaryEnemy);

        for (var j = 0; j < b.Enemies.Count; j++)
        {
            var enemy = b.Enemies[j];
            if (!enemy.IsAlive) continue;
            var json = Describe.Lead("i", j, Describe.Creature(enemy));
            json["intents"] = Intents(enemy, b.Cs);
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

    /// <summary>Sum of attack intents as the game computes them against the local player.</summary>
    private static int? IncomingDamage(Board b)
    {
        try
        {
            var targets = b.Cs.Players.Select(p => p.Creature).ToList();
            var total = 0;
            foreach (var enemy in b.Enemies)
            {
                if (!enemy.IsAlive || enemy.Monster?.NextMove is not { } move || move.Id == "STUNNED") continue;
                foreach (var intent in move.Intents)
                {
                    // VERIFY: AttackIntent.GetTotalDamage returns a number castable to int.
                    if (intent is AttackIntent attack) total += (int)attack.GetTotalDamage(targets, enemy);
                }
            }
            return total;
        }
        catch
        {
            return null;
        }
    }

    private static JsonArray Teammates(Board b)
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
                // VERIFY: orb models expose Id.Entry, and PassiveVal/EvokeVal are numbers castable to int.
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
            // VERIFY: PlayerCombatState.Pets enumerates Creature (Necrobinder's Osty).
            foreach (Creature pet in pcs.Pets) arr.Add(Describe.Creature(pet));
            return arr.Count > 0 ? arr : null;
        }
        catch
        {
            return null;
        }
    }

    // VERIFY: the Regent's character model class is named Regent.
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

    /// <summary>Docs section c: a living, hittable enemy the targeting hooks allow.</summary>
    internal static bool IsTargetableEnemy(ICombatState cs, Creature enemy) =>
        enemy.IsAlive && enemy.IsHittable
        && Hook.ShouldAllowTargeting(cs, enemy, out _); // VERIFY: takes ICombatState (or needs the concrete CombatState).

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
