using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;
using SpireMind.Patches;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// Every overlay that asks the local player to pick cards outside the hand (docs/game-api.md
/// section e): the deck grids for removal, upgrade, transform and enchant, the simple and
/// combat-pile grids, the choose-a-card row and the choose-a-bundle screen. One snapshot
/// shape for all of them: state.kind says which ("remove", "upgrade", "transform", "enchant",
/// "select", "combat_pile_select", "choose", "bundle"), state.cards lists what can be picked.
///
/// Actions: "card:&lt;n&gt;" picks or toggles state.cards entry n (identical unselected copies are
/// listed once, under the first copy's index), "confirm" finishes (on deck screens it confirms
/// the previewed pick, so those take two actions), "cancel" backs out of a preview or closes the
/// screen when the game allows it, "skip" on a skippable choose-a-card row, and "bundle:&lt;n&gt;"
/// then "confirm" on the bundle screen.
///
/// These screens are local to the chooser, so there is no co-op vote here: once our final
/// click lands the screen closes and whatever was waiting on it (an event, a card) carries on.
/// </summary>
internal sealed class CardSelectReader : IScreen
{
    public static readonly CardSelectReader Instance = new();

    private const string ScreenName = "card_select";

    public string Name => ScreenName;

    private static readonly TimeSpan PromptMaxAge = TimeSpan.FromSeconds(30);

    /// <summary>Overlays slide in; a press during the opening tween can be dropped.</summary>
    private static readonly TimeSpan OpenSettle = TimeSpan.FromMilliseconds(300);

    /// <summary>The choose-a-card row ignores presses for 350 ms after it opens.</summary>
    private static readonly TimeSpan ChooseSettle = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan PreviewSettle = TimeSpan.FromMilliseconds(250);

    /// <summary>How long after a pick we expect its preview to show before offering the grid again.</summary>
    private static readonly TimeSpan PreviewExpect = TimeSpan.FromSeconds(1.5);

    /// <summary>A preview whose confirm never enables is stuck; stop waiting on it (the maximum still guards the grid).</summary>
    private static readonly TimeSpan PreviewStuck = TimeSpan.FromSeconds(4);

    /// <summary>After a final click we stop offering that screen's actions; if it is still up this much later, the click didn't land.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    /// <summary>A grid at its maximum with no confirm closes by itself; still up after this long, the maximum we read is wrong.</summary>
    private static readonly TimeSpan FullGrace = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan PressTimeout = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PressMinWait = TimeSpan.FromMilliseconds(150);

    private CardSelectReader() { }

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (node is null || !OnTop(node))
            return GameSnapshot.Busy(ScreenName, "selection screen closing");
        if (ctx.Me is null)
            return GameSnapshot.Busy(ScreenName, "run loading");

        var memo = MemoFor(node);
        return node switch
        {
            NChooseACardSelectionScreen choose => CaptureChoose(ctx, choose, memo),
            NChooseABundleSelectionScreen bundles => CaptureBundles(ctx, bundles, memo),
            _ => CaptureGrid(ctx, node, memo),
        };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (node is null || !OnTop(node))
            return ActionResult.Fail("The selection screen is gone.");
        if (ctx.Me is null)
            return ActionResult.Fail("No run in progress.");

        var memo = MemoFor(node);
        return node switch
        {
            NChooseACardSelectionScreen choose => ExecuteChoose(ctx, choose, memo, actionId),
            NChooseABundleSelectionScreen bundles => ExecuteBundles(ctx, bundles, memo, actionId),
            _ => ExecuteGrid(ctx, node, memo, actionId),
        };
    }

    // ---------- per-screen memory ----------

    /// <summary>What we know about the selection screen now on top, keyed by its instance id.</summary>
    private sealed class Memo
    {
        public required ulong Id { get; init; }
        public required DateTime FirstSeen { get; init; }

        /// <summary>The CardSelectCmd factory that opened this screen, when ChoicePrompt saw it.</summary>
        public string? PromptSource { get; set; }
        public string? Prompt { get; set; }

        /// <summary>The loc key of the screen's own prompt, which tells a removal from other deck picks.</summary>
        public string? PromptKey { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }
        public bool PrefsDone { get; set; }
        public JsonObject? Enchantment { get; set; }

        /// <summary>When the current preview (or bundle confirm) first showed; null while none is up.</summary>
        public DateTime? PreviewSince { get; set; }
        public DateTime? PreviewExpectedUntil { get; set; }

        /// <summary>When we made the click that should close this screen.</summary>
        public DateTime? FinishedAt { get; set; }

        /// <summary>When the selection first reached the known maximum with no confirm to press; null otherwise.</summary>
        public DateTime? FullSince { get; set; }
        public bool MaxDistrusted { get; set; }

        /// <summary>Cards we pressed an odd number of times; stands in for the selection when the screen's can't be read.</summary>
        public List<CardModel> Toggled { get; } = new();

        /// <summary>Guards the reflection press path, which has no node for <see cref="Clicks"/> to latch on.</summary>
        public CardModel? LastPressed { get; set; }
        public DateTime LastPressedAt { get; set; }

        /// <summary>Name of the card we picked last, for confirm labels when the selection can't be read.</summary>
        public string? Picked { get; set; }
        public int? PickedBundle { get; set; }
    }

    private static Memo? _memo;

    /// <summary>The prompt record a screen already took, so a later screen opened without a factory doesn't inherit its min/max.</summary>
    private static (ChoicePrompt.Info Info, ulong ScreenId)? _promptTaken;

    private static Memo MemoFor(Node screen)
    {
        var id = screen.GetInstanceId();
        if (_memo is { } known && known.Id == id) return known;

        var memo = new Memo { Id = id, FirstSeen = DateTime.UtcNow };
        // The CardSelectCmd factories run just before their screen opens. Take the prompt now and
        // keep it, so neither an older prompt nor a later nested one is mistaken for this screen's.
        if (ChoicePrompt.Recent(PromptMaxAge) is { } info && PromptFits(screen, info.Source)
            && (_promptTaken is not { } taken || !ReferenceEquals(taken.Info, info) || taken.ScreenId == id))
        {
            memo.PromptSource = info.Source;
            memo.Prompt = info.Prompt;
            memo.Min = info.Min;
            memo.Max = info.Max;
            _promptTaken = (info, id);
        }
        _memo = memo;
        return memo;
    }

    /// <summary>Which ChoicePrompt factories open which screen (derived screen types first, as in <see cref="LayoutFor"/>).</summary>
    private static bool PromptFits(Node screen, string source) => screen switch
    {
        NChooseACardSelectionScreen => source == "FromChooseACardScreen",
        NChooseABundleSelectionScreen => source == "FromChooseABundleScreen",
        NDeckUpgradeSelectScreen => source == "FromDeckForUpgrade",
        NDeckTransformSelectScreen => source == "FromDeckForTransformation",
        NDeckEnchantSelectScreen => source == "FromDeckForEnchantment",
        NDeckCardSelectScreen => source is "FromDeckForRemoval" or "FromDeckGeneric",
        NCombatPileCardSelectScreen => source == "FromCombatPile",
        NSimpleCardSelectScreen => source.StartsWith("FromSimpleGrid", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>Waits that apply to every kind of selection screen; null when the player may act.</summary>
    private static string? CommonBlocker(GameContext ctx, Node screen, Memo memo, TimeSpan settle)
    {
        if (ActionTracker.Pending is { } pending) return $"resolving {pending}";
        if (screen is CanvasItem ci && !ci.IsVisibleInTree()) return "screen opening";

        var now = DateTime.UtcNow;
        if (now - memo.FirstSeen < settle) return "screen opening";

        if (memo.FinishedAt is { } at)
        {
            if (now - at < RetryAfter)
                return ctx.IsMultiplayer ? "choice sent; waiting for the game and teammates" : "choice sent; waiting for the screen to close";
            ModEntry.Log($"{screen.GetType().Name} still open {RetryAfter.TotalSeconds:0}s after our final click; offering it again");
            memo.FinishedAt = null;
        }
        return null;
    }

    // ---------- grid screens (deck, simple, combat pile) ----------

    /// <summary>
    /// Where a grid screen keeps its confirm steps. Deck screens show the pick in a preview
    /// container with its own confirm and back buttons, looked up from the container as the
    /// screens do; their screen-level confirm (only when min and max differ) opens that preview
    /// early. The simple and combat-pile grids confirm from the screen itself.
    /// </summary>
    private sealed record GridLayout(string[] PreviewContainers, string PreviewConfirm, string PreviewBack, string? Confirm, bool ConfirmIsFinal)
    {
        public bool HasPreview => PreviewContainers.Length > 0;
    }

    private static readonly GridLayout DeckLayout = new(["%PreviewContainer"], "%PreviewConfirm", "%PreviewCancel", "%Confirm", ConfirmIsFinal: false);

    // One pick uses the single container, several the multi one (shown at exactly the maximum); no screen-level confirm.
    private static readonly GridLayout UpgradeLayout = new(
        ["%UpgradeSinglePreviewContainer", "%UpgradeMultiPreviewContainer"], "Confirm", "Cancel", null, ConfirmIsFinal: false);

    // The transform and enchant screens' own confirm is the direct child "Confirm", not a unique name.
    private static readonly GridLayout TransformLayout = new(["%PreviewContainer"], "Confirm", "Cancel", "Confirm", ConfirmIsFinal: false);

    private static readonly GridLayout EnchantLayout = new(
        ["%EnchantSinglePreviewContainer", "%EnchantMultiPreviewContainer"], "Confirm", "Cancel", "Confirm", ConfirmIsFinal: false);

    private static readonly GridLayout PlainLayout = new([], "Confirm", "Cancel", "%Confirm", ConfirmIsFinal: true);

    // Every grid screen derives from NCardGridSelectionScreen directly, so the order doesn't matter.
    private static GridLayout LayoutFor(Node screen) => screen switch
    {
        NDeckUpgradeSelectScreen => UpgradeLayout,
        NDeckTransformSelectScreen => TransformLayout,
        NDeckEnchantSelectScreen => EnchantLayout,
        NDeckCardSelectScreen => DeckLayout,
        _ => PlainLayout,
    };

    private static string KindOf(GameContext ctx, Node screen, Memo memo) => screen switch
    {
        NDeckUpgradeSelectScreen => "upgrade",
        NDeckTransformSelectScreen => "transform",
        NDeckEnchantSelectScreen => "enchant",
        NDeckCardSelectScreen => IsRemoval(ctx, memo) ? "remove" : "select",
        NCombatPileCardSelectScreen => "combat_pile_select",
        _ => "select",
    };

    /// <summary>
    /// The generic deck screen is a removal when its prompt is a removal prompt: every
    /// FromDeckForRemoval caller passes TO_REMOVE, except the combat reward's own key. The factory
    /// record can't tell, since FromDeckForRemoval runs through FromDeckGeneric, which records last.
    /// Without the key, fall back to the shop or the prompt text.
    /// </summary>
    private static bool IsRemoval(GameContext ctx, Memo memo) => memo.PromptKey switch
    {
        "TO_REMOVE" or "COMBAT_REWARD_CARD_REMOVAL.selectionScreenPrompt" => true,
        not null => false,
        null => memo.PromptSource == "FromDeckForRemoval"
                || ctx.Run?.CurrentRoom?.RoomType == RoomType.Shop
                || memo.Prompt?.Contains("remov", StringComparison.OrdinalIgnoreCase) == true,
    };

    private sealed record Preview(Node? Container, NClickableControl Confirm, NClickableControl? Back);

    private enum PreviewPhase { Closed, Opening, Ready }

    /// <summary>Everything one grid capture or execute reads, fetched once.</summary>
    private sealed class GridView
    {
        public required string Kind { get; init; }
        public required GridLayout Layout { get; init; }
        public required NCardGrid? Grid { get; init; }

        /// <summary>The cards "card:&lt;n&gt;" indexes, in display order (sorted when any come from the draw pile).</summary>
        public required List<CardModel> Cards { get; init; }

        /// <summary>One description per card; <see cref="CardEntries"/> consumes them.</summary>
        public required List<JsonObject> Descs { get; init; }

        public required bool Partial { get; init; }
        public required List<CardModel> Selected { get; init; }
        public required Preview? Preview { get; init; }
        public required NClickableControl? Confirm { get; init; }
        public required NClickableControl? Close { get; init; }
        public required int? Min { get; init; }
        public required int? Max { get; init; }

        public bool IsSelected(CardModel card) => Selected.Any(c => ReferenceEquals(c, card));

        public bool Full => Max is { } max && max > 0 && Selected.Count >= max;
    }

    private sealed record Pick(int Index, CardModel Card, string Name, bool Selected);

    private static GameSnapshot CaptureGrid(GameContext ctx, Node screen, Memo memo)
    {
        var now = DateTime.UtcNow;
        var view = ReadGrid(ctx, screen, memo);
        var phase = ResolvePreview(view, memo, now);

        var state = new JsonObject { ["kind"] = view.Kind };
        AddPrompt(state, memo);
        if (ctx.InCombat && view.Cards.Select(c => c.Pile?.Type).Distinct().ToList() is [{ } onlyPile])
            state["pile"] = onlyPile.ToString();
        if (memo.Enchantment is { } enchantment) state["enchantment"] = enchantment.DeepClone();
        if (view.Selected.Count > 0) state["selected"] = Names(view.Selected);

        if (phase != PreviewPhase.Closed && view.Preview is { } preview)
        {
            // For upgrades this is where the upgraded text shows up.
            var shown = new JsonArray();
            foreach (var card in CardsUnder(preview.Container)) shown.Add(CardJson(ctx, card));
            state["preview"] = shown.Count > 0 ? (JsonNode)shown : "open";
        }
        else if (view.Layout.HasPreview)
        {
            state["hint"] = "pick a card, then confirm it on the preview";
        }

        state["cards"] = CardEntries(view, out var picks);
        if (view.Partial) state["partial"] = "only the cards scrolled into view are listed";
        AddContext(state, ctx, view.Cards);

        var waiting = CommonBlocker(ctx, screen, memo, OpenSettle);
        var actions = new List<LegalAction>();
        if (waiting is null)
        {
            switch (phase)
            {
                case PreviewPhase.Opening:
                    waiting = "preview opening";
                    break;

                case PreviewPhase.Ready:
                    actions.Add(new LegalAction("confirm", ConfirmLabel(view, memo)));
                    if (PreviewBack(view) is not null)
                        actions.Add(new LegalAction("cancel", "Back to the cards (pick a different one)"));
                    else if (Usable(view.Close))
                        actions.Add(new LegalAction("cancel", CloseLabel(view.Kind)));
                    break;

                default:
                    if (memo.PreviewExpectedUntil is { } until && now < until)
                    {
                        waiting = "preview opening";
                        break;
                    }

                    var confirmReady = ConfirmReady(view);
                    var atMax = AtMax(view, memo, now);
                    if (atMax && !confirmReady)
                    {
                        // Simple and combat-pile grids finish by themselves once the maximum is picked;
                        // deck screens open their preview instead.
                        waiting = view.Layout.HasPreview ? "selection complete; waiting for the preview" : "selection complete; screen closing";
                        break;
                    }

                    foreach (var pick in picks)
                    {
                        if (pick.Selected || !atMax)
                            actions.Add(new LegalAction($"card:{pick.Index}", PickLabel(view.Kind, pick.Name, pick.Selected)));
                    }
                    if (confirmReady) actions.Add(new LegalAction("confirm", ConfirmLabel(view, memo)));
                    if (Usable(view.Close)) actions.Add(new LegalAction("cancel", CloseLabel(view.Kind)));
                    if (actions.Count == 0) waiting = "nothing to pick yet";
                    break;
            }
        }

        return Snapshot(state, waiting, actions);
    }

    private static ActionResult ExecuteGrid(GameContext ctx, Node screen, Memo memo, string actionId)
    {
        if (CommonBlocker(ctx, screen, memo, OpenSettle) is { } why)
            return ActionResult.Fail($"Cannot choose now ({why}).");

        var now = DateTime.UtcNow;
        var view = ReadGrid(ctx, screen, memo);
        var phase = ResolvePreview(view, memo, now);

        return actionId switch
        {
            "confirm" => ConfirmGrid(screen, memo, view, phase),
            "cancel" => CancelGrid(screen, memo, view, phase),
            _ => PickGrid(screen, memo, view, phase, actionId, now),
        };
    }

    private static ActionResult ConfirmGrid(Node screen, Memo memo, GridView view, PreviewPhase phase)
    {
        var button = phase switch
        {
            PreviewPhase.Ready => view.Preview?.Confirm,
            PreviewPhase.Closed when ConfirmReady(view) => view.Confirm,
            _ => null,
        };
        if (!Usable(button))
            return ActionResult.Fail(phase == PreviewPhase.Opening ? "The preview is still opening." : "The confirm button is not enabled.");

        var what = ConfirmLabel(view, memo);
        if (phase == PreviewPhase.Ready || view.Layout.ConfirmIsFinal)
            return Finish(Click(button, "the confirm button"), screen, memo, what);

        // A screen-level confirm on a deck screen (multi-pick) leads to its preview, not the end.
        var before = Signature(screen, view.Layout);
        var clicked = Click(button, "the confirm button");
        if (clicked.Ok) memo.PreviewExpectedUntil = DateTime.UtcNow + PreviewExpect;
        return Track(clicked, screen, view.Layout, before, what, StepTimeout);
    }

    private static ActionResult CancelGrid(Node screen, Memo memo, GridView view, PreviewPhase phase)
    {
        if (phase != PreviewPhase.Closed && PreviewBack(view) is { } back)
        {
            var before = Signature(screen, view.Layout);
            var clicked = Click(back, "the preview's back button");
            if (clicked.Ok)
            {
                memo.Picked = null;
                memo.Toggled.Clear();
            }
            return Track(clicked, screen, view.Layout, before, "backing out of the preview", StepTimeout);
        }

        if (Usable(view.Close))
            return Finish(Click(view.Close, "the close button"), screen, memo, "closing the selection");

        return ActionResult.Fail("There is no enabled way to back out of this screen.");
    }

    private static ActionResult PickGrid(Node screen, Memo memo, GridView view, PreviewPhase phase, string actionId, DateTime now)
    {
        if (!TryIndex(actionId, "card:", out var n))
            return ActionResult.Fail($"Unknown card selection action '{actionId}'.");
        if (phase != PreviewPhase.Closed)
            return ActionResult.Fail("A preview is open; confirm or cancel it first.");
        if (memo.PreviewExpectedUntil is { } until && now < until)
            return ActionResult.Fail("The preview is still opening.");
        if (n < 0 || n >= view.Cards.Count)
            return ActionResult.Fail($"There is no card {n}.");

        var card = view.Cards[n];
        var selected = view.IsSelected(card);
        if (!selected && AtMax(view, memo, now))
            return ActionResult.Fail("The maximum is already selected; deselect a card first.");

        var name = Describe.CardName(card);
        var before = Signature(screen, view.Layout);
        var pressed = PressCard(screen, view.Grid, card, memo, name);
        if (!pressed.Ok) return pressed;

        if (memo.Toggled.RemoveAll(c => ReferenceEquals(c, card)) == 0)
            memo.Toggled.Add(card);
        if (!selected)
        {
            memo.Picked = name;
            // A single pick (or the last of several) opens the preview; don't offer the grid meanwhile.
            if (view.Layout.HasPreview && (view.Max is null or 1 || view.Selected.Count + 1 >= view.Max))
                memo.PreviewExpectedUntil = now + PreviewExpect;
        }
        else if (memo.Picked == name)
        {
            memo.Picked = null;
        }

        return Track(pressed, screen, view.Layout, before, selected ? $"deselecting {name}" : $"picking {name}", PressTimeout);
    }

    /// <summary>
    /// Press a card the way the grid does: its holder's Pressed signal. The grid is virtualized,
    /// so a card scrolled out of view has no holder; then call the screen's own click handler.
    /// </summary>
    private static ActionResult PressCard(Node screen, NCardGrid? grid, CardModel card, Memo memo, string name)
    {
        NCardHolder? holder = null;
        try
        {
            holder = grid?.GetCardHolder(card); // null when the card's row isn't allocated
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"GetCardHolder: {ex.Message}");
        }
        if (grid is null)
            holder ??= Nodes.FindAll<NCardHolder>(screen).FirstOrDefault(h => GodotObject.IsInstanceValid(h) && ReferenceEquals(h.CardModel, card));

        if (holder is not null && GodotObject.IsInstanceValid(holder))
        {
            var target = holder;
            return Clicks.Once(target, () => target.EmitSignal(NCardHolder.SignalName.Pressed, target), name);
        }

        var now = DateTime.UtcNow;
        if (ReferenceEquals(memo.LastPressed, card) && now - memo.LastPressedAt < Clicks.Window)
            return ActionResult.Fail($"{name} was already clicked; waiting for the game to react.");

        memo.LastPressed = card;
        memo.LastPressedAt = now;
        try
        {
            Nodes.CallPrivate(screen, "OnCardClicked", card);
            return ActionResult.Success;
        }
        catch (Exception ex)
        {
            memo.LastPressed = null;
            var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
            return ActionResult.Fail($"Could not pick {name}: {inner.GetType().Name}: {inner.Message}");
        }
    }

    private static GridView ReadGrid(GameContext ctx, Node screen, Memo memo)
    {
        FillFromPrefs(screen, memo);
        if (screen is NDeckEnchantSelectScreen && memo.Enchantment is null)
            memo.Enchantment = ReadEnchantment(screen);

        var grid = Nodes.Get<NCardGrid>(screen, "%CardGrid");
        var cards = ReadCards(screen, grid, out var partial);
        var descs = cards.Select(c => CardJson(ctx, c)).ToList();
        // Cards gathered from several combat piles say which; a single pile goes in state.pile.
        if (ctx.InCombat && cards.Select(c => c.Pile?.Type).Distinct().Count() > 1)
        {
            for (var i = 0; i < cards.Count; i++)
                if (cards[i].Pile?.Type is { } pile) descs[i]["pile"] = pile.ToString();
        }
        if (cards.Any(InDrawPile)) HideDrawOrder(cards, descs);

        var selected = SelectedCards(screen)
            ?? memo.Toggled.Where(t => cards.Any(c => ReferenceEquals(c, t))).ToList();

        var layout = LayoutFor(screen);
        return new GridView
        {
            Kind = KindOf(ctx, screen, memo),
            Layout = layout,
            Grid = grid,
            Cards = cards,
            Descs = descs,
            Partial = partial,
            Selected = selected,
            Preview = FindPreview(screen, layout),
            Confirm = ScreenConfirm(screen, layout),
            Close = Nodes.Get<NClickableControl>(screen, "%Close"), // an NBackButton on the deck screens, enabled only when cancelable
            Min = memo.Min,
            Max = memo.Max,
        };
    }

    /// <summary>
    /// The grid's full card list. Prefer the grid's own list (display order); fall back to the
    /// screen's (empty on the combat-pile screen); as a last resort take whatever holders exist,
    /// which for a virtualized grid is only the part in view.
    /// </summary>
    private static List<CardModel> ReadCards(Node screen, NCardGrid? grid, out bool partial)
    {
        partial = false;
        // NCardGrid and NCardGridSelectionScreen both keep their list in a protected "_cards".
        if (grid is not null && CardsIn(grid, "_cards") is { Count: > 0 } shown) return shown;
        if (CardsIn(screen, "_cards") is { Count: > 0 } source) return source;

        partial = grid is not null;
        var cards = new List<CardModel>();
        foreach (var holder in Nodes.FindAll<NCardHolder>((Node?)grid ?? screen))
        {
            if (GodotObject.IsInstanceValid(holder) && holder.CardModel is { } card && !cards.Any(c => ReferenceEquals(c, card)))
                cards.Add(card);
        }
        return cards;
    }

    /// <summary>The picks so far: every grid screen keeps them in a private HashSet "_selectedCards".</summary>
    private static List<CardModel>? SelectedCards(Node? owner)
    {
        if (owner is null || !GodotObject.IsInstanceValid(owner)) return null;
        return CardsIn(owner, "_selectedCards");
    }

    /// <summary>The screen-level confirm, if the layout has one.</summary>
    private static NClickableControl? ScreenConfirm(Node screen, GridLayout layout) =>
        layout.Confirm is { } path ? Nodes.Get<NClickableControl>(screen, path) : null;

    /// <summary>
    /// Whether the screen-level confirm may be pressed now. The enchant screen's opens a preview
    /// of the selection that can't work without a pick (with one allowed it throws reading the
    /// first card, with several its Confirm does nothing), so there it waits for a card.
    /// </summary>
    private static bool ConfirmReady(GridView view) =>
        Usable(view.Confirm) && (view.Kind != "enchant" || view.Selected.Count > 0);

    /// <summary>
    /// The open preview's back button, when pressing it is safe. The deck and transform screens'
    /// back handlers refocus the last pick and throw on an empty "choose none" preview, so that
    /// one can only be confirmed.
    /// </summary>
    private static NClickableControl? PreviewBack(GridView view) =>
        view.Preview?.Back is { } back && Usable(back) && view.Selected.Count > 0 ? back : null;

    private static Preview? FindPreview(Node screen, GridLayout layout)
    {
        foreach (var path in layout.PreviewContainers)
        {
            // The screens show a preview by making its container visible (the previewed cards sit under it).
            if (Nodes.Get<Node>(screen, path) is not { } box || !Visible(box)) continue;
            if (Nodes.Get<NClickableControl>(box, layout.PreviewConfirm) is { } confirm && Visible(confirm))
                return new Preview(box, confirm, Nodes.Get<NClickableControl>(box, layout.PreviewBack));
        }
        return null;
    }

    private static PreviewPhase ResolvePreview(GridView view, Memo memo, DateTime now)
    {
        // The container is the screen's own preview flag; it can open with nothing picked (a
        // screen-level confirm when the minimum is 0), which is a real "choose none" preview.
        if (view.Preview is not { } preview)
        {
            if (memo.PreviewSince is not null)
            {
                // Backing out of a preview drops the pick, so our own record of presses is stale.
                memo.PreviewSince = null;
                memo.Picked = null;
                memo.Toggled.Clear();
            }
            return PreviewPhase.Closed;
        }

        memo.PreviewExpectedUntil = null;
        var since = memo.PreviewSince ??= now;
        if (Usable(preview.Confirm) && now - since >= PreviewSettle) return PreviewPhase.Ready;
        return now - since > PreviewStuck ? PreviewPhase.Closed : PreviewPhase.Opening;
    }

    /// <summary>
    /// Whether the known maximum stops further picks. With an enabled confirm it always does, and
    /// on deck screens too: they open their preview at the maximum and keep taking presses past
    /// it (a second card on a single upgrade gets upgraded as well). Otherwise the grid should
    /// close by itself; if it is still up after <see cref="FullGrace"/> the maximum came from
    /// somewhere wrong, so stop trusting it rather than wait forever.
    /// </summary>
    private static bool AtMax(GridView view, Memo memo, DateTime now)
    {
        if (!view.Full || Usable(view.Confirm) || view.Layout.HasPreview)
        {
            memo.FullSince = null;
            return view.Full;
        }
        if (memo.MaxDistrusted) return false;

        var since = memo.FullSince ??= now;
        if (now - since < FullGrace) return true;

        ModEntry.Log($"selection screen still open {FullGrace.TotalSeconds:0}s after reaching max {view.Max}; offering every card again");
        memo.MaxDistrusted = true;
        return false;
    }

    /// <summary>Something that changes when a press or step registers, for the tracker.</summary>
    private static string Signature(Node screen, GridLayout layout)
    {
        if (!GodotObject.IsInstanceValid(screen)) return "gone";
        var preview = FindPreview(screen, layout);
        var selected = SelectedCards(screen)?.Count ?? -1;
        var confirm = Usable(ScreenConfirm(screen, layout));
        return $"{preview is not null}/{Usable(preview?.Confirm)}/{selected}/{confirm}";
    }

    /// <summary>
    /// The cards as JSON plus what can be pressed. Unselected identical copies are listed once,
    /// under the first copy's index with "copies": pressing any copy does the same thing, and a
    /// whole deck would otherwise fill the context with Strikes.
    /// </summary>
    private static JsonArray CardEntries(GridView view, out List<Pick> picks)
    {
        picks = new List<Pick>();
        var entries = new JsonArray();
        var groups = new Dictionary<string, (JsonObject Entry, int Count)>(StringComparer.Ordinal);

        for (var i = 0; i < view.Cards.Count; i++)
        {
            var card = view.Cards[i];
            var desc = view.Descs[i];
            var selected = view.IsSelected(card);
            if (selected)
            {
                var chosen = Describe.Lead("i", i, desc);
                chosen["selected"] = true;
                entries.Add(chosen);
            }
            else
            {
                var key = desc.ToJsonString();
                if (groups.TryGetValue(key, out var group))
                {
                    groups[key] = (group.Entry, group.Count + 1);
                    continue;
                }
                var entry = Describe.Lead("i", i, desc);
                groups[key] = (entry, 1);
                entries.Add(entry);
            }
            picks.Add(new Pick(i, card, Describe.CardName(card), selected));
        }

        foreach (var (first, count) in groups.Values)
        {
            if (count > 1) first["copies"] = count;
        }
        return entries;
    }

    private static string PickLabel(string kind, string name, bool selected) =>
        selected ? $"Deselect {name}" : kind switch
        {
            "remove" or "upgrade" or "transform" or "enchant" => $"Pick {name} to {kind}",
            _ => $"Select {name}",
        };

    private static string ConfirmLabel(GridView view, Memo memo)
    {
        var names = view.Selected.Select(Describe.CardName).ToList();
        if (names.Count == 0 && memo.Picked is { } picked) names.Add(picked);
        var what = string.Join(", ", names);
        return view.Kind switch
        {
            "remove" or "upgrade" or "transform" or "enchant" => what.Length > 0 ? $"Confirm: {view.Kind} {what}" : $"Confirm without choosing a card to {view.Kind}",
            _ => what.Length > 0 ? $"Confirm selection: {what}" : "Confirm with nothing selected",
        };
    }

    private static string CloseLabel(string kind) =>
        kind == "remove" ? "Close without removing a card" : "Close without choosing";

    /// <summary>
    /// The grid screens keep the CardSelectorPrefs they were opened with in a private "_prefs"
    /// (set before the screen is pushed); the choose and bundle screens have none.
    /// </summary>
    private static void FillFromPrefs(Node screen, Memo memo)
    {
        if (memo.PrefsDone) return;
        memo.PrefsDone = true;
        if (FindField(screen.GetType(), "_prefs")?.GetValue(screen) is not CardSelectorPrefs prefs) return;

        memo.PromptKey = prefs.Prompt?.LocEntryKey;
        // The screen's own limits win over the factory record matched by timing.
        memo.Min = prefs.MinSelect;
        memo.Max = prefs.MaxSelect;
        try
        {
            if (memo.Prompt is null && Describe.Text(prefs.Prompt) is { Length: > 0 } prompt) memo.Prompt = prompt;
        }
        catch
        {
            // A prompt needing variables we can't supply; the cards still show.
        }
    }

    /// <summary>
    /// NDeckEnchantSelectScreen keeps the canonical enchantment and its amount privately. Describe a
    /// mutable copy with the amount applied, as the screen's own panel does.
    /// </summary>
    private static JsonObject? ReadEnchantment(Node screen)
    {
        try
        {
            if (Nodes.GetPrivate(screen, "_enchantment") is not EnchantmentModel canonical) return null;
            var shown = canonical.ToMutable();
            if (Nodes.GetPrivate(screen, "_enchantmentAmount") is int amount) shown.Amount = amount;
            shown.RecalculateValues();

            var json = new JsonObject { ["name"] = Describe.Text(shown.Title) };
            var text = Describe.Text(shown.DynamicDescription);
            if (text.Length > 0) json["text"] = text;
            return json;
        }
        catch
        {
            return null;
        }
    }

    // ---------- choose a card ----------

    private sealed record ChooseOption(CardModel Card, NGridCardHolder Holder);

    private static GameSnapshot CaptureChoose(GameContext ctx, NChooseACardSelectionScreen screen, Memo memo)
    {
        var options = ChooseOptions(ctx, screen, out var descs);
        var skip = SkipButton(screen);

        var state = new JsonObject { ["kind"] = "choose" };
        AddPrompt(state, memo);
        var cards = new JsonArray();
        for (var i = 0; i < options.Count; i++) cards.Add(Describe.Lead("i", i, descs[i]));
        state["cards"] = cards;
        state["can_skip"] = skip is not null;
        AddContext(state, ctx, null);

        var waiting = CommonBlocker(ctx, screen, memo, ChooseSettle);
        var actions = new List<LegalAction>();
        if (waiting is null)
        {
            for (var i = 0; i < options.Count; i++)
                actions.Add(new LegalAction($"card:{i}", $"Take {Describe.CardName(options[i].Card)}"));
            if (skip is not null) actions.Add(new LegalAction("skip", "Skip"));
            if (actions.Count == 0) waiting = "cards still being dealt";
        }
        return Snapshot(state, waiting, actions);
    }

    private static ActionResult ExecuteChoose(GameContext ctx, NChooseACardSelectionScreen screen, Memo memo, string actionId)
    {
        if (CommonBlocker(ctx, screen, memo, ChooseSettle) is { } why)
            return ActionResult.Fail($"Cannot choose now ({why}).");

        if (actionId == "skip")
        {
            if (SkipButton(screen) is not { } skip)
                return ActionResult.Fail("This choice cannot be skipped now.");
            return Finish(Click(skip, "the skip button"), screen, memo, "skipping the card choice");
        }

        if (!TryIndex(actionId, "card:", out var n))
            return ActionResult.Fail($"Unknown card choice action '{actionId}'.");

        var options = ChooseOptions(ctx, screen, out _);
        if (n < 0 || n >= options.Count)
            return ActionResult.Fail($"There is no card {n}.");

        var (card, holder) = options[n];
        var name = Describe.CardName(card);
        var pressed = Clicks.Once(holder, () => holder.EmitSignal(NCardHolder.SignalName.Pressed, holder), name);
        return Finish(pressed, screen, memo, $"taking {name}");
    }

    private static List<ChooseOption> ChooseOptions(GameContext ctx, NChooseACardSelectionScreen screen, out List<JsonObject> descs)
    {
        var options = new List<ChooseOption>();
        foreach (var holder in Nodes.FindAll<NGridCardHolder>(Nodes.Get<Node>(screen, "CardRow")))
        {
            if (GodotObject.IsInstanceValid(holder) && holder.CardModel is { } card)
                options.Add(new ChooseOption(card, holder));
        }
        descs = options.Select(o => CardJson(ctx, o.Card)).ToList();
        if (options.Any(o => InDrawPile(o.Card))) HideDrawOrder(options, descs);
        return options;
    }

    private static NClickableControl? SkipButton(NChooseACardSelectionScreen screen)
    {
        // FromChooseACardScreen's canSkip, kept in a private bool; without it the button is hidden and disabled anyway.
        if (Nodes.GetPrivate(screen, "_canSkip") is false) return null;
        var button = Nodes.Get<NChoiceSelectionSkipButton>(screen, "SkipButton");
        return Usable(button) ? button : null;
    }

    // ---------- choose a bundle ----------

    private sealed record Bundle(NCardBundle Node, NClickableControl Hitbox, IReadOnlyList<CardModel> Cards);

    private static GameSnapshot CaptureBundles(GameContext ctx, NChooseABundleSelectionScreen screen, Memo memo)
    {
        var now = DateTime.UtcNow;
        var bundles = FindBundles(screen);
        var phase = BundlePhase(screen, bundles, memo, now);

        var state = new JsonObject { ["kind"] = "bundle" };
        AddPrompt(state, memo);
        var list = new JsonArray();
        for (var n = 0; n < bundles.Count; n++)
        {
            var cards = new JsonArray();
            foreach (var card in bundles[n].Cards) cards.Add(CardJson(ctx, card));
            list.Add(new JsonObject { ["n"] = n, ["cards"] = cards });
        }
        state["bundles"] = list;
        if (phase != PreviewPhase.Closed && memo.PickedBundle is { } picked) state["picked"] = picked;
        else state["hint"] = "pick a bundle, then confirm it";
        AddContext(state, ctx, null);

        var waiting = CommonBlocker(ctx, screen, memo, OpenSettle);
        var actions = new List<LegalAction>();
        if (waiting is null)
        {
            switch (phase)
            {
                case PreviewPhase.Opening:
                    waiting = "bundle preview opening";
                    break;

                case PreviewPhase.Ready:
                    actions.Add(new LegalAction("confirm", BundleConfirmLabel(bundles, memo.PickedBundle)));
                    if (BundleCancel(screen) is not null)
                        actions.Add(new LegalAction("cancel", "Back to the bundles (pick a different one)"));
                    break;

                default:
                    for (var n = 0; n < bundles.Count; n++)
                    {
                        if (bundles[n].Hitbox.IsEnabled && Visible(bundles[n].Node))
                            actions.Add(new LegalAction($"bundle:{n}", $"Pick the bundle with {BundleNames(bundles[n])}"));
                    }
                    if (actions.Count == 0) waiting = "bundles still being dealt";
                    break;
            }
        }
        return Snapshot(state, waiting, actions);
    }

    private static ActionResult ExecuteBundles(GameContext ctx, NChooseABundleSelectionScreen screen, Memo memo, string actionId)
    {
        if (CommonBlocker(ctx, screen, memo, OpenSettle) is { } why)
            return ActionResult.Fail($"Cannot choose now ({why}).");

        var now = DateTime.UtcNow;
        var bundles = FindBundles(screen);
        var phase = BundlePhase(screen, bundles, memo, now);

        if (actionId == "confirm")
        {
            var confirm = BundleConfirm(screen);
            if (phase != PreviewPhase.Ready || !Usable(confirm))
                return ActionResult.Fail("No bundle is ready to confirm.");
            return Finish(Click(confirm, "the confirm button"), screen, memo, BundleConfirmLabel(bundles, memo.PickedBundle));
        }

        if (actionId == "cancel")
        {
            if (phase == PreviewPhase.Closed || BundleCancel(screen) is not { } cancel)
                return ActionResult.Fail("There is no bundle preview to back out of.");
            var clicked = Click(cancel, "the cancel button");
            if (clicked.Ok)
            {
                memo.PickedBundle = null;
                ActionTracker.Begin("backing out of the bundle", _ => !OnTop(screen) || !Usable(BundleConfirm(screen)), StepTimeout, PressMinWait);
            }
            return clicked;
        }

        if (!TryIndex(actionId, "bundle:", out var n))
            return ActionResult.Fail($"Unknown bundle action '{actionId}'.");
        if (phase != PreviewPhase.Closed)
            return ActionResult.Fail("A bundle is already picked; confirm or cancel it first.");
        if (n < 0 || n >= bundles.Count)
            return ActionResult.Fail($"There is no bundle {n}.");

        var hitbox = bundles[n].Hitbox;
        if (!hitbox.IsEnabled)
            return ActionResult.Fail("That bundle cannot be picked right now.");

        var pressed = Click(hitbox, $"bundle {n}");
        if (pressed.Ok)
        {
            memo.PickedBundle = n;
            ActionTracker.Begin($"picking bundle {n}", _ => !OnTop(screen) || Usable(BundleConfirm(screen)), StepTimeout, PressMinWait);
        }
        return pressed;
    }

    private static NClickableControl? BundleConfirm(NChooseABundleSelectionScreen screen) =>
        GodotObject.IsInstanceValid(screen) ? Nodes.Get<NClickableControl>(screen, "%Confirm") : null;

    private static NClickableControl? BundleCancel(NChooseABundleSelectionScreen screen)
    {
        var button = Nodes.Get<NClickableControl>(screen, "%Cancel"); // enabled only while a bundle is previewed
        return Usable(button) ? button : null;
    }

    /// <summary>
    /// Ready once a pick has enabled the confirm button and the preview has settled. The pick is
    /// the one the screen has open (its private "_selectedBundle", whoever clicked it), so the
    /// confirm label names what will actually be taken; our own click stands in if that can't be read.
    /// </summary>
    private static PreviewPhase BundlePhase(NChooseABundleSelectionScreen screen, List<Bundle> bundles, Memo memo, DateTime now)
    {
        if (Nodes.TryGetPrivate(screen, "_selectedBundle") is NCardBundle open)
        {
            var shown = bundles.FindIndex(b => ReferenceEquals(b.Node, open));
            if (shown >= 0) memo.PickedBundle = shown;
        }
        if (memo.PickedBundle is null || !Usable(BundleConfirm(screen)))
        {
            memo.PreviewSince = null;
            return PreviewPhase.Closed;
        }
        var since = memo.PreviewSince ??= now;
        return now - since >= PreviewSettle ? PreviewPhase.Ready : PreviewPhase.Opening;
    }

    /// <summary>
    /// The bundles in display order: the NCardBundle children of "%BundleRow", one per offered
    /// bundle in the factory's order. Their card list stays put while a picked bundle's card
    /// nodes move to the preview.
    /// </summary>
    private static List<Bundle> FindBundles(NChooseABundleSelectionScreen screen)
    {
        var found = new List<Bundle>();
        foreach (var node in Nodes.FindAll<NCardBundle>(Nodes.Get<Node>(screen, "%BundleRow")))
        {
            if (GodotObject.IsInstanceValid(node) && node.Hitbox is { } hitbox && GodotObject.IsInstanceValid(hitbox))
                found.Add(new Bundle(node, hitbox, node.Bundle));
        }
        return found;
    }

    private static string BundleNames(Bundle bundle) =>
        bundle.Cards.Count > 0 ? string.Join(", ", bundle.Cards.Select(Describe.CardName)) : "unknown cards";

    private static string BundleConfirmLabel(List<Bundle> bundles, int? picked) =>
        picked is { } n && n >= 0 && n < bundles.Count ? $"Take the bundle with {BundleNames(bundles[n])}" : "Take the picked bundle";

    // ---------- state helpers ----------

    private static GameSnapshot Snapshot(JsonObject state, string? waiting, List<LegalAction> actions) =>
        waiting is null
            ? new GameSnapshot { Screen = ScreenName, AwaitingDecision = true, State = state, Actions = actions }
            : new GameSnapshot { Screen = ScreenName, AwaitingDecision = false, Waiting = waiting, State = state };

    private static void AddPrompt(JsonObject state, Memo memo)
    {
        if (memo.Prompt is { Length: > 0 } prompt) state["prompt"] = prompt;
        if (memo.Min is { } min) state["min"] = min;
        if (memo.Max is { } max) state["max"] = max;
    }

    /// <summary>
    /// What the choice depends on beyond the cards: the fight in combat; otherwise HP, gold,
    /// relics, potions and the deck (left out when <paramref name="shown"/> already is the deck).
    /// </summary>
    private static void AddContext(JsonObject state, GameContext ctx, IReadOnlyCollection<CardModel>? shown)
    {
        if (ctx.Run is { } rs) state["run"] = Describe.RunInfo(rs);
        if (CombatSummary(ctx) is { } combat)
        {
            state["combat"] = combat;
            return;
        }
        if (ctx.Me is not { } me) return;

        var deck = me.Deck.Cards;
        var listed = shown is null ? null : new HashSet<CardModel>(shown, ReferenceEqualityComparer.Instance);
        var wholeDeck = listed is not null && deck.Count > 0 && deck.All(listed.Contains);
        var player = Describe.PlayerRun(me, includeDeck: !wholeDeck);
        if (wholeDeck) player["deck_size"] = deck.Count;
        state["player"] = player;
    }

    /// <summary>A small picture of the fight for choices made mid-combat (discover, pick from a pile).</summary>
    private static JsonObject? CombatSummary(GameContext ctx)
    {
        if (!ctx.InCombat || ctx.Me is not { } me || ctx.Combat is not { } cs || ctx.MyCombat is not { } pcs) return null;
        try
        {
            var hand = new JsonArray();
            foreach (var card in pcs.Hand.Cards) hand.Add(Describe.CardName(card));

            var enemies = new JsonArray();
            foreach (var enemy in cs.Enemies)
            {
                if (!enemy.IsAlive) continue;
                var json = Describe.Creature(enemy);
                try
                {
                    json["intents"] = Describe.Intents(enemy, cs);
                }
                catch
                {
                    // Intents are a bonus here; the choice itself is what matters.
                }
                enemies.Add(json);
            }

            return new JsonObject
            {
                ["turn"] = pcs.TurnNumber,
                ["energy"] = pcs.Energy,
                ["max_energy"] = pcs.MaxEnergy,
                ["me"] = Describe.Creature(me.Creature),
                ["hand"] = hand,
                ["draw_count"] = pcs.DrawPile.Cards.Count,
                ["discard_count"] = pcs.DiscardPile.Cards.Count,
                ["enemies"] = enemies,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Live pile text in combat; deck text plus rarity everywhere else.</summary>
    private static JsonObject CardJson(GameContext ctx, CardModel card) =>
        ctx.InCombat ? Describe.Card(card, card.Pile?.Type ?? PileType.Deck) : Describe.CardOffer(card);

    private static JsonArray Names(IEnumerable<CardModel> cards)
    {
        var arr = new JsonArray();
        foreach (var card in cards) arr.Add(Describe.CardName(card));
        return arr;
    }

    private static bool InDrawPile(CardModel card) => card.Pile?.Type == PileType.Draw;

    /// <summary>
    /// Draw order is hidden from human players, so any list holding draw-pile cards is sorted
    /// by what the card looks like. The sort is stable, so the only order kept is among copies
    /// that look identical, which tells nothing.
    /// </summary>
    private static void HideDrawOrder<T>(List<T> items, List<JsonObject> descs)
    {
        var order = Enumerable.Range(0, items.Count).OrderBy(i => descs[i].ToJsonString(), StringComparer.Ordinal).ToList();
        var sortedItems = order.Select(i => items[i]).ToList();
        var sortedDescs = order.Select(i => descs[i]).ToList();
        items.Clear();
        items.AddRange(sortedItems);
        descs.Clear();
        descs.AddRange(sortedDescs);
    }

    /// <summary>
    /// Card models drawn under a node: holders' CardModel, then bare NCards' Model. Only what is
    /// actually shown counts; a hidden node may hold something the player can't see (a stale card).
    /// A random transform's result is shown as a holder cycling through the possible cards, with
    /// its hitbox set to ignore the mouse (the only preview that does); that is no outcome, so skip it.
    /// </summary>
    private static List<CardModel> CardsUnder(Node? root)
    {
        var cards = new List<CardModel>();
        if (root is null || !GodotObject.IsInstanceValid(root)) return cards;

        var cycling = new HashSet<NCard>(ReferenceEqualityComparer.Instance);
        foreach (var holder in Nodes.FindAll<NCardHolder>(root))
        {
            if (holder.Hitbox is { } hitbox && GodotObject.IsInstanceValid(hitbox) && hitbox.MouseFilter == Control.MouseFilterEnum.Ignore)
            {
                if (holder.CardNode is { } stand) cycling.Add(stand);
                continue;
            }
            if (Visible(holder) && holder.CardModel is { } held && !cards.Any(c => ReferenceEquals(c, held)))
                cards.Add(held);
        }
        foreach (var view in Nodes.FindAll<NCard>(root))
        {
            if (!cycling.Contains(view) && Visible(view) && view.Model is { } drawn && !cards.Any(c => ReferenceEquals(c, drawn)))
                cards.Add(drawn);
        }
        return cards;
    }

    // ---------- clicking and tracking ----------

    private static bool OnTop(Node screen) =>
        GodotObject.IsInstanceValid(screen) && ReferenceEquals(Nodes.TopOverlay(), screen);

    // ForceClick skips the enabled check; callers test Usable (or IsEnabled) first.
    private static ActionResult Click(NClickableControl button, string what) =>
        Clicks.Once(button, () => button.ForceClick(), what);

    /// <summary>A click that should close the screen: stop offering it and wait for it to go.</summary>
    private static ActionResult Finish(ActionResult click, Node screen, Memo memo, string what)
    {
        if (!click.Ok) return click;
        memo.FinishedAt = DateTime.UtcNow;
        ActionTracker.Begin(what, _ => !OnTop(screen), FinishTimeout);
        return click;
    }

    /// <summary>A click that moves the screen along (pick, toggle, back): wait for the grid to show it.</summary>
    private static ActionResult Track(ActionResult click, Node screen, GridLayout layout, string before, string what, TimeSpan timeout)
    {
        if (click.Ok)
            ActionTracker.Begin(what, _ => !OnTop(screen) || Signature(screen, layout) != before, timeout, PressMinWait);
        return click;
    }

    /// <summary>Visible in the tree and drawn with some opacity: panels may fade out rather than hide.</summary>
    private static bool Visible([NotNullWhen(true)] Node? node)
    {
        if (node is not CanvasItem ci || !GodotObject.IsInstanceValid(ci) || !ci.IsVisibleInTree()) return false;
        var alpha = 1f;
        // Modulate multiplies down the tree until a CanvasLayer starts a new canvas.
        for (Node? n = ci; n is CanvasItem item; n = n.GetParent())
            alpha *= item.Modulate.A;
        return alpha > 0.05f;
    }

    private static bool Usable([NotNullWhen(true)] NClickableControl? button) =>
        button is not null && Visible(button) && button.IsEnabled;

    private static bool TryIndex(string actionId, string prefix, out int n)
    {
        n = -1;
        return actionId.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(prefix.Length), out n);
    }

    // ---------- reflection over the screens' private state ----------

    // The screens don't publish their card lists, selections or prefs; read the non-public
    // fields by their exact v0.107.1 names, looked up once per type. A name that goes missing
    // after a game update degrades to "unknown" rather than failing the snapshot.

    private static readonly Dictionary<(Type Type, string Name), FieldInfo?> FieldCache = new();

    private static FieldInfo? FindField(Type type, string name)
    {
        if (FieldCache.TryGetValue((type, name), out var cached)) return cached;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        FieldInfo? found = null;
        for (var t = type; t is not null && found is null; t = t.BaseType)
            found = t.GetField(name, flags);

        FieldCache[(type, name)] = found;
        return found;
    }

    private static List<CardModel>? CardsIn(object owner, string field) =>
        FindField(owner.GetType(), field)?.GetValue(owner) is IEnumerable<CardModel> cards ? cards.Where(c => c is not null).ToList() : null;
}
