using System.Collections;
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

    /// <summary>A preview whose confirm never enables is more likely a misread panel than a real one; stop waiting on it.</summary>
    private static readonly TimeSpan PreviewStuck = TimeSpan.FromSeconds(4);

    /// <summary>How long to keep looking for a screen's selector prefs before deciding it has none.</summary>
    private static readonly TimeSpan PrefsPatience = TimeSpan.FromSeconds(2);

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
    /// Where a grid screen keeps its confirm step. Deck screens show the pick in a preview
    /// with its own Confirm; the simple and combat-pile grids confirm from the screen itself.
    /// </summary>
    private sealed record GridLayout(string[] PreviewContainers, string? PreviewConfirm, string? PreviewBack, bool ConfirmIsFinal)
    {
        public bool HasPreview => PreviewContainers.Length > 0 || PreviewConfirm is not null;
    }

    private static readonly GridLayout DeckLayout = new([], "%PreviewConfirm", "%PreviewCancel", ConfirmIsFinal: false); // VERIFY: NDeckCardSelectScreen's preview back button is "%PreviewCancel" (only offered when found)

    private static readonly GridLayout UpgradeLayout = new(
        ["%UpgradeSinglePreviewContainer", "%UpgradeMultiPreviewContainer"], null, null, ConfirmIsFinal: false); // VERIFY: a multi-card upgrade uses "%UpgradeMultiPreviewContainer" (by analogy with enchant)

    private static readonly GridLayout TransformLayout = new(["%PreviewContainer"], null, null, ConfirmIsFinal: false);

    private static readonly GridLayout EnchantLayout = new(
        ["%EnchantSinglePreviewContainer", "%EnchantMultiPreviewContainer"], null, null, ConfirmIsFinal: false);

    private static readonly GridLayout PlainLayout = new([], null, null, ConfirmIsFinal: true);

    // Derived screen types first, in case the deck screens share NDeckCardSelectScreen as a base.
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
    /// The generic deck screen is a removal when FromDeckForRemoval opened it; without a factory
    /// record, when it is in the shop or its prompt says so.
    /// </summary>
    private static bool IsRemoval(GameContext ctx, Memo memo) => memo.PromptSource switch
    {
        "FromDeckForRemoval" => true,
        "FromDeckGeneric" => false,
        _ => ctx.Run?.CurrentRoom?.RoomType == RoomType.Shop
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
                    if (Usable(view.Preview?.Back))
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

                    var confirmReady = Usable(view.Confirm);
                    var atMax = AtMax(view, memo, now);
                    if (atMax && !confirmReady)
                    {
                        // Simple and combat-pile grids finish by themselves once the maximum is picked.
                        waiting = "selection complete; screen closing";
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
            PreviewPhase.Closed => view.Confirm,
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
        if (phase != PreviewPhase.Closed && view.Preview?.Back is { } back && Usable(back))
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
            holder = grid?.GetCardHolder(card) as NCardHolder; // VERIFY: NCardGrid.GetCardHolder(CardModel) returns the holder (NCardHolder or a subclass), null when scrolled out of view
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
        var now = DateTime.UtcNow;
        FillFromPrefs(screen, memo, now);
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

        var selected = SelectedCards(screen) ?? SelectedCards(grid)
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
            Confirm = Nodes.Get<NClickableControl>(screen, "%Confirm"), // VERIFY: deck screens with several picks confirm from "%Confirm" too (documented only for the simple/combat-pile grids)
            Close = Nodes.Get<NClickableControl>(screen, "%Close"), // VERIFY: "%Close" is an NClickableControl (NBackButton-style) on the deck screens
            Min = memo.Min,
            Max = memo.Max,
        };
    }

    /// <summary>
    /// The grid's full card list. Prefer the grid's own list (display order); fall back to the
    /// screen's; as a last resort take whatever holders exist, which for a virtualized grid is
    /// only the part in view.
    /// </summary>
    private static List<CardModel> ReadCards(Node screen, NCardGrid? grid, out bool partial)
    {
        partial = false;
        if (grid is not null && CardsIn(grid, AllCardsField(grid.GetType())) is { Count: > 0 } shown) return shown;
        if (CardsIn(screen, AllCardsField(screen.GetType())) is { Count: > 0 } source) return source;

        partial = grid is not null;
        var cards = new List<CardModel>();
        foreach (var holder in Nodes.FindAll<NCardHolder>((Node?)grid ?? screen))
        {
            if (GodotObject.IsInstanceValid(holder) && holder.CardModel is { } card && !cards.Any(c => ReferenceEquals(c, card)))
                cards.Add(card);
        }
        return cards;
    }

    // VERIFY: NCardGrid (or else the screen) keeps every card it shows in an IEnumerable<CardModel> field, ideally "_cards"
    private static FieldInfo? AllCardsField(Type type) =>
        FindField(type, "cards",
            f => IsCardList(f.FieldType) && !NameHas(f, "selected", "chosen", "picked", "preview", "visible", "hover"),
            "_cards", "_allCards", "<Cards>k__BackingField", "_cardModels");

    // VERIFY: the grid screens keep the picked-so-far cards (or holders) in a field whose name contains "selected", e.g. "_selectedCards"
    private static List<CardModel>? SelectedCards(Node? owner)
    {
        if (owner is null || !GodotObject.IsInstanceValid(owner)) return null;
        var field = FindField(owner.GetType(), "selected",
            f => (IsCardList(f.FieldType) || IsHolderList(f.FieldType)) && NameHas(f, "selected", "chosen", "picked"),
            "_selectedCards", "_selected", "<SelectedCards>k__BackingField");

        return field?.GetValue(owner) switch
        {
            IEnumerable<CardModel> cards => cards.Where(c => c is not null).ToList(),
            IEnumerable<NCardHolder> holders => holders.Where(h => GodotObject.IsInstanceValid(h)).Select(h => h.CardModel).OfType<CardModel>().ToList(),
            _ => null,
        };
    }

    private static Preview? FindPreview(Node screen, GridLayout layout)
    {
        foreach (var path in layout.PreviewContainers)
        {
            if (Nodes.Get<Node>(screen, path) is not { } box || !Visible(box)) continue;
            // A container can stay visible while empty; it counts as open only with its Confirm showing.
            if (ButtonIn(box, "Confirm") is { } confirm && Visible(confirm))
                return new Preview(box, confirm, ButtonIn(box, "Cancel")); // VERIFY: the preview containers' back button is named "Cancel" (only offered when found)
        }

        if (layout.PreviewConfirm is { } confirmPath && Nodes.Get<NClickableControl>(screen, confirmPath) is { } direct && Visible(direct))
        {
            var back = layout.PreviewBack is { } backPath ? Nodes.Get<NClickableControl>(screen, backPath) : null;
            return new Preview(Nodes.Get<Node>(screen, "%PreviewContainer"), direct, back); // VERIFY: NDeckCardSelectScreen's previewed card sits under "%PreviewContainer" (only used to describe it)
        }
        return null;
    }

    private static PreviewPhase ResolvePreview(GridView view, Memo memo, DateTime now)
    {
        // A preview only follows a pick; a panel that looks open with nothing picked is a misread.
        if (view.Preview is not { } preview || (view.Selected.Count == 0 && memo.Picked is null))
        {
            if (memo.PreviewSince is not null)
            {
                // Backing out of a preview drops the pick, so our own record of presses is stale.
                memo.PreviewSince = null;
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
    /// Whether the known maximum stops further picks. With an enabled confirm it always does.
    /// Without one the grid should close by itself; if it is still up after <see cref="FullGrace"/>
    /// the maximum came from somewhere wrong, so stop trusting it rather than wait forever.
    /// </summary>
    private static bool AtMax(GridView view, Memo memo, DateTime now)
    {
        if (!view.Full || Usable(view.Confirm))
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
        var selected = (SelectedCards(screen) ?? SelectedCards(Nodes.Get<NCardGrid>(screen, "%CardGrid")))?.Count ?? -1;
        var confirm = Usable(Nodes.Get<NClickableControl>(screen, "%Confirm"));
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
            "remove" or "upgrade" or "transform" or "enchant" => what.Length > 0 ? $"Confirm: {view.Kind} {what}" : $"Confirm the {view.Kind}",
            _ => what.Length > 0 ? $"Confirm selection: {what}" : "Confirm selection",
        };
    }

    private static string CloseLabel(string kind) =>
        kind == "remove" ? "Close without removing a card" : "Close without choosing";

    private static void FillFromPrefs(Node screen, Memo memo, DateTime now)
    {
        if (memo.PrefsDone) return;

        // VERIFY: the selection screens keep their CardSelectorPrefs in a field (type name contains "Prefs") with Prompt, MinSelect and MaxSelect
        var field = FindField(screen.GetType(), "prefs", f => Underlying(f.FieldType).Name.Contains("Prefs", StringComparison.Ordinal));
        var prefs = field?.GetValue(screen);
        if (prefs is null)
        {
            // The field may only be filled once the screen is set up; give it a moment.
            if (field is null || now - memo.FirstSeen > PrefsPatience) memo.PrefsDone = true;
            return;
        }

        memo.PrefsDone = true;
        memo.Prompt ??= ReadText(prefs, "Prompt", "Title", "Header");
        memo.Min ??= ReadInt(prefs, "MinSelect", "MinCount", "Min");
        memo.Max ??= ReadInt(prefs, "MaxSelect", "MaxCount", "Max");
    }

    // VERIFY: NDeckEnchantSelectScreen holds the enchantment it applies in a field whose type name contains "Enchantment", with Title and Description
    private static JsonObject? ReadEnchantment(Node screen)
    {
        try
        {
            var field = FindField(screen.GetType(), "enchantment",
                f => Underlying(f.FieldType).Name.Contains("Enchantment", StringComparison.Ordinal) && !typeof(IEnumerable).IsAssignableFrom(f.FieldType));
            if (field?.GetValue(screen) is not { } enchantment) return null;

            var json = new JsonObject { ["name"] = Describe.Text(Nodes.GetPrivate(enchantment, "Title")) };
            var text = ReadText(enchantment, "Description");
            if (text is { Length: > 0 }) json["text"] = text;
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
            if (GodotObject.IsInstanceValid(holder) && holder.CardModel is { } card) // VERIFY: NGridCardHolder exposes CardModel (inherited from NCardHolder)
                options.Add(new ChooseOption(card, holder));
        }
        descs = options.Select(o => CardJson(ctx, o.Card)).ToList();
        if (options.Any(o => InDrawPile(o.Card))) HideDrawOrder(options, descs);
        return options;
    }

    private static NClickableControl? SkipButton(NChooseACardSelectionScreen screen)
    {
        if (Nodes.GetPrivate(screen, "_canSkip") is false) return null; // VERIFY: the screen's canSkip flag is a bool field named _canSkip (the visible, enabled button is checked regardless)
        var button = Nodes.Get<NClickableControl>(screen, "SkipButton"); // VERIFY: "SkipButton" derives from NClickableControl
        return Usable(button) ? button : null;
    }

    // ---------- choose a bundle ----------

    private sealed record Bundle(Node Node, NClickableControl Hitbox, List<CardModel> Cards);

    private static GameSnapshot CaptureBundles(GameContext ctx, NChooseABundleSelectionScreen screen, Memo memo)
    {
        var now = DateTime.UtcNow;
        FillFromPrefs(screen, memo, now);
        var bundles = FindBundles(screen);
        var phase = BundlePhase(screen, memo, now);

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
        var phase = BundlePhase(screen, memo, now);
        var bundles = FindBundles(screen);

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
        var button = Nodes.Get<NClickableControl>(screen, "%Cancel"); // VERIFY: the bundle screen's back-out button is "%Cancel" (only offered when found)
        return Usable(button) ? button : null;
    }

    /// <summary>Ready once our pick has enabled the confirm button and the preview has settled.</summary>
    private static PreviewPhase BundlePhase(NChooseABundleSelectionScreen screen, Memo memo, DateTime now)
    {
        if (memo.PickedBundle is null || !Usable(BundleConfirm(screen)))
        {
            memo.PreviewSince = null;
            return PreviewPhase.Closed;
        }
        var since = memo.PreviewSince ??= now;
        return now - since >= PreviewSettle ? PreviewPhase.Ready : PreviewPhase.Opening;
    }

    /// <summary>
    /// The bundles in display order: from the screen's own list when it has one, else every
    /// node in the tree that looks like a bundle (has "Bundle" in its type name and a Hitbox),
    /// outer ones only and none inside a preview, which may show a copy of the picked bundle.
    /// </summary>
    private static List<Bundle> FindBundles(NChooseABundleSelectionScreen screen)
    {
        IEnumerable<Node> candidates;
        // VERIFY: bundle nodes have "Bundle" in their type name and expose Hitbox as an NClickableControl
        var listField = FindField(screen.GetType(), "bundles",
            f => ElementType(f.FieldType) is { } e && typeof(Node).IsAssignableFrom(e) && e.Name.Contains("Bundle", StringComparison.Ordinal));
        if (listField?.GetValue(screen) is IEnumerable fromField)
            candidates = fromField.OfType<Node>().ToList();
        else
            candidates = Nodes.FindAll<Node>(screen).Where(n => n.GetType().Name.Contains("Bundle", StringComparison.Ordinal) && !InsidePreview(n, screen));

        var found = new List<Bundle>();
        foreach (var node in candidates)
        {
            if (!GodotObject.IsInstanceValid(node) || found.Any(b => b.Node.IsAncestorOf(node))) continue;
            if (Nodes.GetPrivate(node, "Hitbox") is not NClickableControl hitbox || !GodotObject.IsInstanceValid(hitbox)) continue;
            found.Add(new Bundle(node, hitbox, BundleCards(node)));
        }
        return found;
    }

    private static List<CardModel> BundleCards(Node bundle)
    {
        var field = FindField(bundle.GetType(), "bundle_cards", f => IsCardList(f.FieldType), "_cards", "<Cards>k__BackingField");
        if (CardsIn(bundle, field) is { Count: > 0 } cards) return cards;
        return CardsUnder(bundle);
    }

    private static bool InsidePreview(Node node, Node screen)
    {
        for (var n = node.GetParent(); n is not null && !ReferenceEquals(n, screen); n = n.GetParent())
        {
            if (n.Name.ToString().Contains("Preview", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
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

    private static bool InDrawPile(CardModel card) => card.Pile?.Type == PileType.Draw; // VERIFY: the draw pile's PileType member is named Draw

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
    /// actually shown counts; a hidden node may hold something the player can't see (a pre-rolled
    /// transform result, a stale card).
    /// </summary>
    private static List<CardModel> CardsUnder(Node? root)
    {
        var cards = new List<CardModel>();
        if (root is null || !GodotObject.IsInstanceValid(root)) return cards;

        foreach (var holder in Nodes.FindAll<NCardHolder>(root))
        {
            if (Visible(holder) && holder.CardModel is { } held && !cards.Any(c => ReferenceEquals(c, held)))
                cards.Add(held);
        }
        foreach (var view in Nodes.FindAll<NCard>(root))
        {
            if (Visible(view) && view.Model is { } drawn && !cards.Any(c => ReferenceEquals(c, drawn))) // VERIFY: NCard exposes its CardModel as Model
                cards.Add(drawn);
        }
        return cards;
    }

    // ---------- clicking and tracking ----------

    private static bool OnTop(Node screen) =>
        GodotObject.IsInstanceValid(screen) && ReferenceEquals(Nodes.TopOverlay(), screen);

    // VERIFY: the screens' buttons and hitboxes derive from NClickableControl (IsEnabled, ForceClick), like the other clickables
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

    /// <summary>A button inside a preview container: the direct child first, else a descendant by name.</summary>
    private static NClickableControl? ButtonIn(Node box, string name)
    {
        if (box.GetNodeOrNull(name) is NClickableControl direct) return direct;
        NClickableControl? loose = null;
        foreach (var button in Nodes.FindAll<NClickableControl>(box))
        {
            var own = button.Name.ToString();
            if (own == name) return button;
            if (loose is null && own.Contains(name, StringComparison.OrdinalIgnoreCase)) loose = button;
        }
        return loose;
    }

    private static bool TryIndex(string actionId, string prefix, out int n)
    {
        n = -1;
        return actionId.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(prefix.Length), out n);
    }

    // ---------- reflection over the screens' private state ----------

    // The screens don't publish their card lists, selections or prefs, so find them by field
    // type (and a name hint) once per type. Everything here degrades to "unknown" when a guess
    // misses rather than failing the snapshot.

    private static readonly Dictionary<(Type Type, string Role), FieldInfo?> FieldCache = new();

    private static FieldInfo? FindField(Type type, string role, Func<FieldInfo, bool> match, params string[] preferred)
    {
        if (FieldCache.TryGetValue((type, role), out var cached)) return cached;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var candidates = new List<FieldInfo>();
        // Godot's own classes hold none of this; stop at them.
        for (var t = type; t is not null && t.Assembly != typeof(Node).Assembly; t = t.BaseType)
            candidates.AddRange(t.GetFields(flags).Where(match));

        FieldInfo? pick = null;
        foreach (var name in preferred)
        {
            pick = candidates.FirstOrDefault(f => f.Name == name);
            if (pick is not null) break;
        }
        pick ??= candidates.FirstOrDefault();

        FieldCache[(type, role)] = pick;
        return pick;
    }

    private static List<CardModel>? CardsIn(object owner, FieldInfo? field) =>
        field?.GetValue(owner) is IEnumerable<CardModel> cards ? cards.Where(c => c is not null).ToList() : null;

    private static bool IsCardList(Type t) => t != typeof(string) && typeof(IEnumerable<CardModel>).IsAssignableFrom(t);

    private static bool IsHolderList(Type t) => typeof(IEnumerable<NCardHolder>).IsAssignableFrom(t);

    private static bool NameHas(FieldInfo f, params string[] parts) =>
        parts.Any(p => f.Name.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static Type Underlying(Type t) => Nullable.GetUnderlyingType(t) ?? t;

    private static Type? ElementType(Type t)
    {
        if (t == typeof(string)) return null;
        if (t.IsArray) return t.GetElementType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return t.GetGenericArguments()[0];
        return t.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static string? ReadText(object owner, params string[] members)
    {
        foreach (var member in members)
        {
            try
            {
                var text = Nodes.GetPrivate(owner, member) switch
                {
                    LocString loc => Describe.Text(loc),
                    string s => Describe.Text(s),
                    _ => "",
                };
                if (text.Length > 0) return text;
            }
            catch
            {
                // Some LocStrings need variables we can't supply; try the next member.
            }
        }
        return null;
    }

    private static int? ReadInt(object owner, params string[] members)
    {
        foreach (var member in members)
        {
            if (Nodes.GetPrivate(owner, member) is int value) return value;
        }
        return null;
    }
}
