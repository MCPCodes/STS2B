namespace SpireMind.Arena;

/// <summary>Which of the game's selection screens a choice uses; it decides the snapshot's screen and action ids.</summary>
public enum ChoiceKind
{
    /// <summary>Cards from the hand: screen "hand_select", "select:&lt;handIndex&gt;" then "confirm".</summary>
    HandSelect,

    /// <summary>One upgradable card from the hand (Armaments): the same screen in "upgrade" mode.</summary>
    HandUpgrade,

    /// <summary>Cards from the draw, discard or exhaust pile: screen "card_select", kind "combat_pile_select", "card:&lt;n&gt;".</summary>
    CombatPile,

    /// <summary>One of up to three offered cards: screen "card_select", kind "choose", "card:&lt;n&gt;" or "skip".</summary>
    Choose,

    /// <summary>Cards from any list in a plain grid: screen "card_select", kind "select".</summary>
    Grid,
}

/// <summary>A choice the player has been asked and has not finished answering.</summary>
internal sealed class PendingChoice
{
    public required ChoiceKind Kind { get; init; }
    public string? Prompt { get; init; }
    public required int Min { get; init; }
    public required int Max { get; init; }
    public bool CanSkip { get; init; }

    /// <summary>The candidates in the order the screen lists them.</summary>
    public required List<CardInstance> Options { get; init; }

    /// <summary>Indexes into <see cref="Options"/>, in the order picked.</summary>
    public List<int> Selected { get; init; } = new();

    /// <summary>The game only shows a confirm button on these screens when min and max differ.</summary>
    public bool ManualConfirm => Min != Max;

    public bool IsHand => Kind is ChoiceKind.HandSelect or ChoiceKind.HandUpgrade;

    public PendingChoice CloneFor(CloneMap map) => new()
    {
        Kind = Kind, Prompt = Prompt, Min = Min, Max = Max, CanSkip = CanSkip,
        // Offered cards that are in no pile yet (a card potion's three) are not in the map: they are shared, and never changed.
        Options = Options.Select(c => map.Get(c)!).ToList(),
        Selected = Selected.ToList(),
    };
}

/// <summary>Thrown to unwind an action that needs an answer the player has not given yet. Engine
/// and content code must never catch it.</summary>
internal sealed class ChoicePendingException : Exception;

// Cards the player must pick in the middle of an effect (the game's CardSelectCmd).
//
// An effect asks with one of the Choose methods and gets the picked cards back, as if the
// call had simply waited. Underneath, the first time the answer is not known yet the whole
// action is abandoned and the fight shows the selection screen; once the player has
// answered, the action is run again from a copy of the fight taken before it started, and
// this time the call returns the answer. The engine is deterministic, so the second run is
// the first one continued. For content that means: keep every bit of state inside the fight
// (fields of powers, monsters, cards), never in statics or captured variables.
public sealed partial class Fight
{
    private FightState? _checkpoint;
    private string? _pendingAction;
    private List<int[]> _answers = new();
    private int _answerCursor;
    private PendingChoice? _pending;
    private List<(int AfterAnswer, int Seed)> _rerandomize = new();
    private bool _running;

    /// <summary>The player is in the middle of a card choice.</summary>
    public bool HasPendingChoice => _pending is not null;

    /// <summary>
    /// The player picks cards from the hand (the game's FromHand). With no candidate nothing is
    /// asked; with min = max and no more candidates than that, all are taken without asking.
    /// Returns the picked cards; empty while the fight is over or ending.
    /// </summary>
    /// <param name="prompt">What the screen asks, in our own words ("Choose a card to Exhaust.").</param>
    /// <param name="min">Fewest cards.</param>
    /// <param name="max">Most cards.</param>
    /// <param name="filter">Which hand cards qualify; null for all.</param>
    public IReadOnlyList<CardInstance> ChooseFromHand(string prompt, int min, int max, Func<CardInstance, bool>? filter = null)
    {
        if (IsOverOrEnding) return [];
        var candidates = _s.Hand.Where(c => filter?.Invoke(c) ?? true).ToList();
        if (candidates.Count == 0) return candidates;
        if (min == max && candidates.Count <= min) return candidates;
        return Ask(new PendingChoice { Kind = ChoiceKind.HandSelect, Prompt = prompt, Min = min, Max = max, Options = candidates });
    }

    /// <summary>The player picks one card from the hand to exhaust: FromHand with the game's exhaust prompt, exactly one card.</summary>
    public CardInstance? ChooseHandCardToExhaust() => ChooseFromHand("Choose a card to Exhaust.", 1, 1).FirstOrDefault();

    /// <summary>
    /// The player picks one upgradable card from the hand (the game's FromHandForUpgrade): with
    /// none or one candidate nothing is asked. Returns null when there is none.
    /// </summary>
    public CardInstance? ChooseHandCardToUpgrade()
    {
        if (IsOverOrEnding) return null;
        var candidates = _s.Hand.Where(c => c.IsUpgradable).ToList();
        if (candidates.Count <= 1) return candidates.FirstOrDefault();
        return Ask(new PendingChoice { Kind = ChoiceKind.HandUpgrade, Prompt = "Choose a card to Upgrade.", Min = 1, Max = 1, Options = candidates }).FirstOrDefault();
    }

    /// <summary>
    /// The player picks cards from the draw, discard or exhaust pile (the game's FromCombatPile).
    /// The same no-question rules as <see cref="ChooseFromHand"/>. Cards of the draw pile are
    /// listed in an order that gives nothing away.
    /// </summary>
    public IReadOnlyList<CardInstance> ChooseFromPile(PileType pile, string prompt, int min, int max, Func<CardInstance, bool>? filter = null)
    {
        if (IsEnding) return [];
        var candidates = PileList(pile).Where(c => filter?.Invoke(c) ?? true).ToList();
        if (candidates.Count == 0) return candidates;
        if (min == max && candidates.Count <= min) return candidates;
        return Ask(new PendingChoice { Kind = ChoiceKind.CombatPile, Prompt = prompt, Min = min, Max = max, Options = HideDrawOrder(candidates) });
    }

    /// <summary>
    /// The player takes one of up to three offered cards, or skips when allowed (the game's
    /// FromChooseACardScreen; the card potions). Returns null on a skip or an empty offer.
    /// </summary>
    public CardInstance? ChooseOneOf(IReadOnlyList<CardInstance> cards, bool canSkip = false)
    {
        if (cards.Count > 3) throw new ArgumentException("The choose-a-card screen shows at most three cards.", nameof(cards));
        if (cards.Count == 0) return null;
        return Ask(new PendingChoice { Kind = ChoiceKind.Choose, Min = canSkip ? 0 : 1, Max = 1, CanSkip = canSkip, Options = HideDrawOrder(cards.ToList()) }).FirstOrDefault();
    }

    /// <summary>The player picks cards from any list in a plain grid (the game's FromSimpleGrid).</summary>
    public IReadOnlyList<CardInstance> ChooseFromGrid(IReadOnlyList<CardInstance> cards, string prompt, int min, int max)
    {
        if (IsEnding) return [];
        var candidates = cards.ToList();
        if (candidates.Count == 0) return candidates;
        if (min == max && candidates.Count <= min) return candidates;
        return Ask(new PendingChoice { Kind = ChoiceKind.Grid, Prompt = prompt, Min = min, Max = max, Options = HideDrawOrder(candidates) });
    }

    /// <summary>As the mod does: a list holding draw-pile cards is sorted by how the cards look, so their order tells nothing.</summary>
    private List<CardInstance> HideDrawOrder(List<CardInstance> cards)
    {
        if (!cards.Any(c => c.Pile == PileType.Draw)) return cards;
        return cards.OrderBy(c => CardJson(c, c.Pile).ToJsonString(), StringComparer.Ordinal).ToList();
    }

    private IReadOnlyList<CardInstance> Ask(PendingChoice choice)
    {
        if (!_running)
            throw new InvalidOperationException("A card choice was asked for outside a player action (during set-up); the arena cannot show it.");

        if (_answerCursor < _answers.Count)
        {
            var answer = _answers[_answerCursor++];
            var picked = answer.Select(i => choice.Options[i]).ToList();
            // A determinized copy forgets what the player could not know from this point on.
            foreach (var (afterAnswer, seed) in _rerandomize)
                if (afterAnswer == _answerCursor - 1) Rerandomize(seed);
            return picked;
        }

        _pending = choice;
        throw new ChoicePendingException();
    }

    /// <summary>
    /// Replaces what the player cannot know: the order of the draw pile (cards whose place is
    /// known stay put) and every future random number. The hidden cards are first put into an
    /// order that follows only from what they look like, so that the real order leaves no trace:
    /// two fights that show the same snapshot give the same copy for the same seed.
    /// </summary>
    private void Rerandomize(int seed)
    {
        _s.Rng = new RngSet(seed);
        var unknown = new List<int>();
        for (var i = 0; i < _s.Draw.Count; i++)
            if (!_s.Draw[i].KnownPosition) unknown.Add(i);
        var cards = unknown.Select(i => _s.Draw[i])
            .OrderBy(c => CardJson(c, PileType.Draw).ToJsonString(), StringComparer.Ordinal)
            .ThenBy(c => c.Id)
            .ToList();
        RngSet.For(seed, "determinize").Shuffle(cards);
        for (var k = 0; k < unknown.Count; k++) _s.Draw[unknown[k]] = cards[k];
    }
}
