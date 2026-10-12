using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// What a card choice asks for (the game's CardSelectorPrefs): the prompt, the fewest and the
/// most cards, and the two switches the game's screens read. The prompt texts are the arena's
/// own plain words; the numbers and switches are the game's.
/// </summary>
public sealed record CardSelectPrefs
{
    /// <param name="prompt">What the screen asks, in our own words ("Choose a card to remove.").</param>
    /// <param name="min">Fewest cards.</param>
    /// <param name="max">Most cards.</param>
    public CardSelectPrefs(string prompt, int min, int max)
    {
        Prompt = prompt;
        Min = min;
        Max = max;
        RequireManualConfirmation = min >= 0 && min != max;
    }

    /// <summary>Exactly <paramref name="count"/> cards.</summary>
    public CardSelectPrefs(string prompt, int count) : this(prompt, count, count) { }

    public string Prompt { get; }
    public int Min { get; }
    public int Max { get; }

    /// <summary>
    /// The game's default is "when min and max differ". Callers that can be backed out of set it
    /// too (Smith, the shop's removal): it then also stops the game from taking the cards
    /// without asking when there are no more than the minimum to choose from.
    /// </summary>
    public bool RequireManualConfirmation { get; init; }

    /// <summary>The screen has a close button: the player may leave without choosing (Smith, the shop's removal, a card removal reward).</summary>
    public bool Cancelable { get; init; }

    /// <summary>
    /// The prompt is one of the game's two removal prompts. On the generic deck screen that is
    /// what makes the snapshot's kind "remove" instead of "select": the mod reads the screen's
    /// prompt, not who opened it.
    /// </summary>
    public bool IsRemovalPrompt { get; init; }

    /// <summary>The game's removal prompt for <paramref name="count"/> cards.</summary>
    public static CardSelectPrefs Remove(int count) =>
        new(Counted(count, "remove"), count) { IsRemovalPrompt = true };

    /// <summary>The prompt of a card removal reward: one card, and the player may close the screen.</summary>
    public static CardSelectPrefs RemoveForReward() =>
        new(Counted(1, "remove"), 1) { IsRemovalPrompt = true, Cancelable = true, RequireManualConfirmation = true };

    /// <summary>The game's upgrade prompt for <paramref name="count"/> cards.</summary>
    public static CardSelectPrefs Upgrade(int count) => new(Counted(count, "upgrade"), count);

    /// <summary>The game's transform prompt for <paramref name="count"/> cards.</summary>
    public static CardSelectPrefs Transform(int count) => new(Counted(count, "transform"), count);

    private static string Counted(int count, string verb) => count == 1
        ? $"Choose a card to {verb}."
        : string.Create(CultureInfo.InvariantCulture, $"Choose {count} cards to {verb}.");
}

/// <summary>
/// The card choices of a run outside a fight (the game's CardSelectCmd): each method asks the
/// player through the screen the game would open and returns the chosen cards, as if the call
/// had simply waited. Underneath it is <see cref="Run.Ask"/>: the action that calls one of
/// these is abandoned the first time and run again with the answer, so it must keep all its
/// state in the run. An empty answer means nothing was chosen: the player closed the screen,
/// or there was nothing to choose from.
/// <para>
/// As in the game, a choice with no more candidates than the minimum is not asked at all (the
/// candidates are taken), unless the prefs ask for manual confirmation.
/// </para>
/// </summary>
public static class CardSelect
{
    /// <summary>The curses' place in a removal grid: the game sorts them in front of everything else.</summary>
    private const int CursesFirst = -999999999;

    private static RunState Outside(Run run) =>
        run.Fight is null ? run.State : throw new InvalidOperationException("A deck choice of the run is for outside a fight; a fight asks through its own choices.");

    /// <summary>
    /// Cards of the deck to remove (the game's FromDeckForRemoval): every card that is not
    /// Eternal and passes <paramref name="filter"/>, the curses first, then the rest, each group
    /// in deck order. The generic deck screen with its preview; kind "remove" when the prompt is
    /// a removal prompt (<see cref="CardSelectPrefs.Remove"/>).
    /// </summary>
    public static IReadOnlyList<CardInstance> FromDeckForRemoval(Run run, CardSelectPrefs prefs, Func<CardInstance, bool>? filter = null)
    {
        var deck = Outside(run).Deck.ToList();
        return FromDeckGeneric(run, prefs,
            card => DeckEdits.IsRemovable(card) && (filter is null || filter(card)),
            card => card.Type != CardType.Curse ? deck.IndexOf(card) : CursesFirst);
    }

    /// <summary>
    /// Cards of the deck in the generic deck screen with its preview (the game's
    /// FromDeckGeneric): every card that passes <paramref name="filter"/>, in deck order or
    /// sorted by <paramref name="sortingOrder"/> (a stable sort). Nothing is asked, and nothing
    /// chosen, while the player is dead.
    /// </summary>
    public static IReadOnlyList<CardInstance> FromDeckGeneric(Run run, CardSelectPrefs prefs, Func<CardInstance, bool>? filter = null, Func<CardInstance, int>? sortingOrder = null)
    {
        var state = Outside(run);
        var cards = filter is null ? state.Deck.ToList() : state.Deck.Where(filter).ToList();
        if (state.IsDead) return [];
        if (sortingOrder is not null) cards = cards.OrderBy(sortingOrder).ToList();
        if (cards.Count == 0) return [];
        if (!prefs.RequireManualConfirmation && cards.Count <= prefs.Min) return cards;
        return Ask(run, new CardSelectScreen(CardSelectScreen.GridLayout.Deck, prefs, cards), cards);
    }

    /// <summary>Cards of the deck to upgrade (the game's FromDeckForUpgrade): every card that can still be upgraded, in
    /// deck order, in the upgrade screen, whose preview shows the upgraded card.</summary>
    public static IReadOnlyList<CardInstance> FromDeckForUpgrade(Run run, CardSelectPrefs prefs)
    {
        var cards = Outside(run).Deck.Where(card => card.IsUpgradable).ToList();
        if (cards.Count == 0) return [];
        if (cards.Count <= prefs.Min && !prefs.RequireManualConfirmation) return cards;
        return Ask(run, new CardSelectScreen(CardSelectScreen.GridLayout.Upgrade, prefs, cards), cards);
    }

    /// <summary>Cards of the deck to transform (the game's FromDeckForTransformation with a random outcome): every card
    /// that is not a Quest card and not Eternal, in deck order, in the transform screen. The preview never shows what a
    /// card will become.</summary>
    public static IReadOnlyList<CardInstance> FromDeckForTransformation(Run run, CardSelectPrefs prefs)
    {
        var cards = Outside(run).Deck.Where(card => card.Type != CardType.Quest && DeckEdits.IsTransformable(run, card)).ToList();
        if (cards.Count == 0) return [];
        if (cards.Count <= prefs.Min && !prefs.RequireManualConfirmation) return cards;
        return Ask(run, new CardSelectScreen(CardSelectScreen.GridLayout.Transform, prefs, cards), cards);
    }

    /// <summary>
    /// Cards from any list in the plain grid without a preview (the game's FromSimpleGrid and
    /// FromSimpleGridForRewards): the grid of generated cards an event offers. Kind "select"; the
    /// pick that reaches the maximum closes the screen unless the prefs ask for confirmation.
    /// </summary>
    public static IReadOnlyList<CardInstance> FromSimpleGrid(Run run, IReadOnlyList<CardInstance> cards, CardSelectPrefs prefs)
    {
        var listed = cards.ToList();
        if (listed.Count == 0) return [];
        if (!prefs.RequireManualConfirmation && listed.Count <= prefs.Min) return listed;
        return Ask(run, new CardSelectScreen(CardSelectScreen.GridLayout.Simple, prefs, listed), listed);
    }

    /// <summary>
    /// One of up to three offered cards, or none when the choice can be skipped (the game's
    /// FromChooseACardScreen). Kind "choose": one action takes a card, there is no confirm. An
    /// empty offer asks nothing; a single card is still asked.
    /// </summary>
    /// <returns>The card taken, or null on a skip or an empty offer.</returns>
    public static CardInstance? FromChooseACardScreen(Run run, IReadOnlyList<CardInstance> cards, bool canSkip = false)
    {
        if (cards.Count > 3) throw new ArgumentException("The choose-a-card screen shows at most three cards.", nameof(cards));
        if (cards.Count == 0) return null;
        var offered = cards.ToList();
        return run.Ask(new ChooseCardScreen(offered, canSkip)) is { Length: > 0 } answer ? offered[answer[0]] : null;
    }

    private static IReadOnlyList<CardInstance> Ask(Run run, CardSelectScreen screen, List<CardInstance> cards) =>
        run.Ask(screen) is { } answer ? answer.Select(i => cards[i]).ToList() : [];
}

/// <summary>
/// Picks in the order a .NET HashSet lists them, which is the order the game's screens show and
/// hand back their selection in: a new pick takes the place the last deselected one left, or
/// else goes to the end. Holds indexes into a screen's card list.
/// </summary>
internal sealed class SelectionSet
{
    private const int Freed = -1;
    private readonly List<int> _places = new();
    private readonly List<int> _free = new();

    public int Count => _places.Count - _free.Count;

    public bool Contains(int item) => _places.Contains(item);

    /// <summary>False when the item is already in.</summary>
    public bool Add(int item)
    {
        if (Contains(item)) return false;
        if (_free.Count > 0)
        {
            // The place freed last is taken first.
            _places[_free[^1]] = item;
            _free.RemoveAt(_free.Count - 1);
        }
        else
        {
            _places.Add(item);
        }
        return true;
    }

    public bool Remove(int item)
    {
        var place = _places.IndexOf(item);
        if (place < 0) return false;
        _places[place] = Freed;
        _free.Add(place);
        return true;
    }

    /// <summary>Empties the set; the next items start from the first place again.</summary>
    public void Clear()
    {
        _places.Clear();
        _free.Clear();
    }

    public List<int> InOrder() => _places.Where(item => item != Freed).ToList();

    public SelectionSet Clone()
    {
        var copy = new SelectionSet();
        copy._places.AddRange(_places);
        copy._free.AddRange(_free);
        return copy;
    }
}

/// <summary>
/// The grid kinds of the <c>card_select</c> screen outside a fight, as the mod's
/// CardSelectReader shows the game's four grid screens (docs/screens.md, "Card selection"):
/// the generic deck screen (kind <c>remove</c> or <c>select</c>), the upgrade screen
/// (<c>upgrade</c>), the transform screen (<c>transform</c>) and the plain grid
/// (<c>select</c>). It is the question of a run-level card choice (<see cref="Run.Ask"/>): its
/// picks, its preview and backing out of the preview change only the screen; its last step
/// answers with the picked cards' indexes in the game's order, or with null when the player
/// closed it.
/// <para>
/// The three deck screens take two actions for a pick: the pick that reaches the maximum opens
/// a preview (for an upgrade it shows the upgraded card), <c>confirm</c> there finishes and
/// <c>cancel</c> goes back to the cards with the selection dropped. The plain grid has no
/// preview: the pick that reaches the maximum closes it.
/// </para>
/// </summary>
public sealed class CardSelectScreen : RunScreen
{
    /// <summary>Which of the game's grid screens this is; each has its own rules for the preview, the confirm button and the close button.</summary>
    public enum GridLayout
    {
        /// <summary>NDeckCardSelectScreen: removal and every other plain pick from the deck.</summary>
        Deck,

        /// <summary>NDeckUpgradeSelectScreen.</summary>
        Upgrade,

        /// <summary>NDeckTransformSelectScreen.</summary>
        Transform,

        /// <summary>NSimpleCardSelectScreen: a grid of any cards, without a preview or a close button.</summary>
        Simple,
    }

    private const string Hint = "pick a card, then confirm it on the preview";

    private readonly List<CardInstance> _cards;
    private SelectionSet _selected = new();
    private bool _preview;
    private bool _clicked;

    internal CardSelectScreen(GridLayout layout, CardSelectPrefs prefs, List<CardInstance> cards)
    {
        Layout = layout;
        Prefs = prefs;
        _cards = cards;
    }

    public override string Name => "card_select";

    public GridLayout Layout { get; }

    public CardSelectPrefs Prefs { get; }

    /// <summary>The snapshot's <c>kind</c>. The generic deck screen is a removal when its prompt is a removal prompt, as the mod tells it.</summary>
    public string Kind => Layout switch
    {
        GridLayout.Upgrade => "upgrade",
        GridLayout.Transform => "transform",
        GridLayout.Deck when Prefs.IsRemovalPrompt => "remove",
        _ => "select",
    };

    /// <summary>The cards <c>card:&lt;n&gt;</c> indexes, in the order the grid shows them.</summary>
    public IReadOnlyList<CardInstance> Cards => _cards;

    /// <summary>The picked cards in the order the game keeps them.</summary>
    public IReadOnlyList<CardInstance> Selected => _selected.InOrder().Select(i => _cards[i]).ToList();

    /// <summary>The preview of a deck screen is up: only confirm and going back are offered.</summary>
    public bool PreviewOpen => _preview;

    private bool HasPreview => Layout != GridLayout.Simple;

    /// <summary>The mod stops offering picks at the maximum (a maximum of 0 or less never counts as reached).</summary>
    private bool AtMax => Prefs.Max > 0 && _selected.Count >= Prefs.Max;

    /// <summary>The screen-level confirm button is enabled. The upgrade screen has none.</summary>
    private bool GridConfirmReady => Layout switch
    {
        GridLayout.Deck or GridLayout.Transform => Prefs.Min != Prefs.Max && _selected.Count >= Prefs.Min,
        // The plain grid enables it on opening when nothing has to be picked, and after every click by this rule.
        GridLayout.Simple => _clicked ? _selected.Count >= Prefs.Min && Prefs.RequireManualConfirmation : Prefs.Min == 0,
        _ => false,
    };

    /// <summary>The close button is enabled: only on a deck screen that may be backed out of, and never while its preview is up.</summary>
    private bool CloseReady => HasPreview && Prefs.Cancelable && !_preview;

    // ---------- the snapshot (CardSelectReader.CaptureGrid) ----------

    public override GameSnapshot Snapshot(Run run)
    {
        var state = new JsonObject { ["kind"] = Kind };
        if (Prefs.Prompt.Length > 0) state["prompt"] = Prefs.Prompt;
        state["min"] = Prefs.Min;
        state["max"] = Prefs.Max;

        var picked = _selected.InOrder();
        if (picked.Count > 0)
        {
            var names = new JsonArray();
            foreach (var i in picked) names.Add(_cards[i].Title);
            state["selected"] = names;
        }

        if (_preview) state["preview"] = PreviewJson(picked);
        else if (HasPreview) state["hint"] = Hint;

        state["cards"] = Entries(out var picks);
        state["run"] = run.RunJson();
        // A grid that lists every card of the deck leaves the deck's name counts out and keeps deck_size, at the end.
        var deck = run.State.Deck;
        var wholeDeck = deck.Count > 0 && deck.All(card => _cards.Any(listed => ReferenceEquals(listed, card)));
        state["player"] = run.PlayerJson(wholeDeck ? PlayerKeys.FullWithoutDeck : PlayerKeys.Full);

        var actions = new List<LegalAction>();
        if (_preview)
        {
            actions.Add(new LegalAction("confirm", ConfirmLabel(picked)));
            // Going back needs a pick to go back from: a preview of nothing can only be confirmed.
            if (picked.Count > 0) actions.Add(new LegalAction("cancel", "Back to the cards (pick a different one)"));
        }
        else
        {
            var atMax = AtMax;
            foreach (var (index, name, selected) in picks)
                if (selected || !atMax) actions.Add(new LegalAction(Id(index), PickLabel(name, selected)));
            if (GridConfirmReady) actions.Add(new LegalAction("confirm", ConfirmLabel(picked)));
            if (CloseReady) actions.Add(new LegalAction("cancel", CloseLabel));
        }
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    /// <summary>
    /// The cards the preview shows, as the mod reads them off the preview container. The generic
    /// deck screen shows the picked cards. A single upgrade shows the card and then the upgraded
    /// card; several upgrades show only the upgraded cards. A transform shows the picked cards
    /// only: what they become is not shown (the game cycles through the possible cards and the
    /// mod skips that). A preview with no card in it is the text "open".
    /// </summary>
    private JsonNode PreviewJson(List<int> picked)
    {
        var shown = new JsonArray();
        foreach (var i in picked)
        {
            var card = _cards[i];
            var single = Layout == GridLayout.Upgrade && Prefs.Max == 1;
            if (Layout != GridLayout.Upgrade || single) shown.Add(RunDescribe.CardOffer(card));
            if (Layout == GridLayout.Upgrade) shown.Add(RunDescribe.CardOffer(Upgraded(card)));
        }
        return shown.Count > 0 ? shown : "open";
    }

    /// <summary>A copy of the card with one more upgrade, for the preview only; the card itself is not touched.</summary>
    private static CardInstance Upgraded(CardInstance card)
    {
        var copy = card.Duplicate(0);
        if (copy.IsUpgradable) copy.ApplyUpgrade();
        return copy;
    }

    /// <summary>
    /// The card entries: <c>i</c> first, then the card as an offer. A picked card is listed on
    /// its own with <c>selected: true</c>; unpicked cards that look the same are listed once,
    /// under the first copy's index, with <c>copies</c>.
    /// </summary>
    private JsonArray Entries(out List<(int Index, string Name, bool Selected)> picks)
    {
        picks = new List<(int, string, bool)>();
        var entries = new JsonArray();
        var groups = new Dictionary<string, (JsonObject Entry, int Count)>(StringComparer.Ordinal);
        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            var description = RunDescribe.CardOffer(card);
            var selected = _selected.Contains(i);
            if (selected)
            {
                var chosen = Lead(i, description);
                chosen["selected"] = true;
                entries.Add(chosen);
            }
            else
            {
                var key = description.ToJsonString();
                if (groups.TryGetValue(key, out var group))
                {
                    groups[key] = (group.Entry, group.Count + 1);
                    continue;
                }
                var entry = Lead(i, description);
                groups[key] = (entry, 1);
                entries.Add(entry);
            }
            picks.Add((i, card.Title, selected));
        }
        foreach (var (entry, count) in groups.Values)
            if (count > 1) entry["copies"] = count;
        return entries;
    }

    internal static JsonObject Lead(int index, JsonObject body)
    {
        var json = new JsonObject { ["i"] = index };
        foreach (var name in body.Select(kv => kv.Key).ToList())
        {
            var node = body[name];
            body.Remove(name);
            json[name] = node;
        }
        return json;
    }

    internal static string Id(int index) => string.Create(CultureInfo.InvariantCulture, $"card:{index}");

    private string PickLabel(string name, bool selected) =>
        selected ? $"Deselect {name}" : Kind == "select" ? $"Select {name}" : $"Pick {name} to {Kind}";

    private string ConfirmLabel(List<int> picked)
    {
        var names = string.Join(", ", picked.Select(i => _cards[i].Title));
        if (Kind == "select") return names.Length > 0 ? $"Confirm selection: {names}" : "Confirm with nothing selected";
        return names.Length > 0 ? $"Confirm: {Kind} {names}" : $"Confirm without choosing a card to {Kind}";
    }

    private string CloseLabel => Kind == "remove" ? "Close without removing a card" : "Close without choosing";

    // ---------- the actions (CardSelectReader.ExecuteGrid and the screens' own click handlers) ----------

    public override ActionResult Execute(Run run, string actionId) => actionId switch
    {
        "confirm" => Confirm(run),
        "cancel" => Cancel(run),
        _ => Pick(run, actionId),
    };

    private ActionResult Confirm(Run run)
    {
        if (_preview)
        {
            // The preview's confirm is final. The screens guard it with their own counts; a preview is never up below them.
            var enough = Layout == GridLayout.Upgrade ? _selected.Count > 0 && _selected.Count >= Prefs.Max : _selected.Count >= Prefs.Min || Layout == GridLayout.Transform;
            return enough ? Answer(run) : ActionResult.Fail("The confirm button is not enabled.");
        }
        if (!GridConfirmReady) return ActionResult.Fail("The confirm button is not enabled.");

        // The screen-level confirm: final on the plain grid; on a deck screen it opens the preview early.
        if (Layout == GridLayout.Simple || (Layout == GridLayout.Transform && !Prefs.RequireManualConfirmation)) return Answer(run);
        _preview = true;
        return ActionResult.Success;
    }

    private ActionResult Cancel(Run run)
    {
        if (_preview && _selected.Count > 0)
        {
            // Back to the cards: the game drops the whole selection.
            _selected.Clear();
            _preview = false;
            return ActionResult.Success;
        }
        return CloseReady ? run.Answer(null) : ActionResult.Fail("There is no enabled way to back out of this screen.");
    }

    private ActionResult Pick(Run run, string actionId)
    {
        if (!TryIndex(actionId, "card:", out var n)) return ActionResult.Fail($"Unknown card selection action '{actionId}'.");
        if (_preview) return ActionResult.Fail("A preview is open; confirm or cancel it first.");
        if (n < 0 || n >= _cards.Count) return ActionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"There is no card {n}."));
        var selected = _selected.Contains(n);
        if (!selected && AtMax) return ActionResult.Fail("The maximum is already selected; deselect a card first.");

        _clicked = true;
        if (selected)
        {
            _selected.Remove(n);
            return ActionResult.Success;
        }

        if (Layout == GridLayout.Simple)
        {
            if (_selected.Count < Prefs.Max) _selected.Add(n);
            // Without a confirm button the plain grid closes by itself once the maximum is picked.
            return !Prefs.RequireManualConfirmation && _selected.Count >= Prefs.Max ? Answer(run) : ActionResult.Success;
        }

        _selected.Add(n);
        // A single upgrade previews every pick; the other deck screens open the preview when the maximum is reached.
        if ((Layout == GridLayout.Upgrade && Prefs.Max == 1) || _selected.Count == Prefs.Max) _preview = true;
        return ActionResult.Success;
    }

    /// <summary>The picked cards' indexes in the order the game hands its selection back.</summary>
    private ActionResult Answer(Run run) => run.Answer(_selected.InOrder().ToArray());

    internal static bool TryIndex(string actionId, string prefix, out int n)
    {
        n = -1;
        return actionId.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(actionId.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
    }

    public override RunScreen Clone(CloneMap map) =>
        new CardSelectScreen(Layout, Prefs, _cards.Select(card => map.Get(card)!).ToList()) { _selected = _selected.Clone(), _preview = _preview, _clicked = _clicked };
}

/// <summary>
/// The <c>choose</c> kind of the <c>card_select</c> screen outside a fight (the game's
/// choose-a-card row, docs/screens.md): up to three offered cards, one action takes one, and
/// <c>skip</c> takes none when the choice can be skipped. The question of
/// <see cref="CardSelect.FromChooseACardScreen"/>: it answers with the taken card's index, or
/// with nothing on a skip.
/// </summary>
public sealed class ChooseCardScreen : RunScreen
{
    private readonly List<CardInstance> _cards;

    internal ChooseCardScreen(List<CardInstance> cards, bool canSkip)
    {
        _cards = cards;
        CanSkip = canSkip;
    }

    public override string Name => "card_select";

    /// <summary>The offered cards in the order of the row.</summary>
    public IReadOnlyList<CardInstance> Cards => _cards;

    public bool CanSkip { get; }

    public override GameSnapshot Snapshot(Run run)
    {
        var cards = new JsonArray();
        var actions = new List<LegalAction>();
        for (var i = 0; i < _cards.Count; i++)
        {
            cards.Add(CardSelectScreen.Lead(i, RunDescribe.CardOffer(_cards[i])));
            actions.Add(new LegalAction(CardSelectScreen.Id(i), $"Take {_cards[i].Title}"));
        }
        if (CanSkip) actions.Add(new LegalAction("skip", "Skip"));

        // The game hands this screen no prompt; min and max are the mod's own: 0 or 1 by whether it can be skipped, and 1.
        var state = new JsonObject
        {
            ["kind"] = "choose",
            ["min"] = CanSkip ? 0 : 1,
            ["max"] = 1,
            ["cards"] = cards,
            ["can_skip"] = CanSkip,
            ["run"] = run.RunJson(),
            ["player"] = run.PlayerJson(),
        };
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public override ActionResult Execute(Run run, string actionId)
    {
        if (actionId == "skip")
            return CanSkip ? run.Answer([]) : ActionResult.Fail("This choice cannot be skipped now.");
        if (!CardSelectScreen.TryIndex(actionId, "card:", out var n)) return ActionResult.Fail($"Unknown card choice action '{actionId}'.");
        if (n < 0 || n >= _cards.Count) return ActionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"There is no card {n}."));
        return run.Answer([n]);
    }

    public override RunScreen Clone(CloneMap map) => new ChooseCardScreen(_cards.Select(card => map.Get(card)!).ToList(), CanSkip);
}
