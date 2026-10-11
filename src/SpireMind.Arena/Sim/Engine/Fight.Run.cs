namespace SpireMind.Arena;

// What a run hands a fight and takes back when it ends (Sim/Run/Run.Fights.cs does both).
// A single fight has none of it: no deck cards of its own, no gold, three belt slots.
//
// In the game the player is one object for the whole run, so HP, max HP, gold, the belt, the
// relics and the deck are simply still there after a fight. Here a fight works on its own
// copies, because it is copied whole before every action and by players that search; the run
// takes them back when the fight is over.
public sealed partial class Fight
{
    /// <summary>
    /// The player's gold. A single fight starts with 0. The setter is the bare hand-over seam:
    /// gaining and losing gold with their hooks are commands of the relic framework.
    /// </summary>
    public int Gold
    {
        get => _s.Gold;
        internal set => _s.Gold = Math.Max(0, value);
    }

    /// <summary>
    /// Run mode: the player's deck as it stands, in deck order (pile Deck). These are not the
    /// cards in the combat piles: those were made from them when the fight started and point
    /// back through <see cref="CardInstance.DeckCardId"/>. An upgrade made in the fight
    /// (Armaments) changes the combat card only. Empty in a single fight.
    /// </summary>
    public IReadOnlyList<CardInstance> DeckCards => _s.Deck;

    /// <summary>The deck itself, for the commands that change it from inside a fight. What is in
    /// it when the fight ends is the run's deck from then on.</summary>
    internal List<CardInstance> DeckList => _s.Deck;

    /// <summary>
    /// A new deck card (pile Deck) with an id no card of the run has had, in no list yet: the
    /// command that adds a card to the deck from inside a fight puts it into <see cref="DeckList"/>.
    /// </summary>
    internal CardInstance NewDeckCard(CardDef def, bool upgraded = false)
    {
        var card = new CardInstance(_s.NextDeckCardId++, def) { Pile = PileType.Deck };
        if (upgraded && card.IsUpgradable) card.ApplyUpgrade();
        return card;
    }

    /// <summary>
    /// The deck card a combat card was made from (the game's DeckVersion): null for a card made
    /// during the fight, for a copy, when the deck card has left the deck since, and in a single fight.
    /// </summary>
    public CardInstance? DeckVersionOf(CardInstance card)
    {
        if (card.DeckCardId == 0) return null;
        foreach (var deckCard in _s.Deck)
            if (deckCard.Id == card.DeckCardId) return deckCard;
        return null;
    }

    /// <summary>How many monsters left the fight without dying. The share of its gold a fight pays follows from it.</summary>
    public int EscapedCount => _s.Escaped;

    /// <summary>
    /// Step 1 of the set-up in a run: the relics, the belt, the gold and the deck are the run's.
    /// The fight takes its own copies, relics and deck cards together, so that a relic that
    /// remembers a deck card points at the fight's copy of it.
    /// </summary>
    private void TakeFromRun(RunHandOver run)
    {
        var map = new CloneMap();
        _s.Relics.Clear();
        foreach (var relic in run.Relics) _s.Relics.Add(relic.CloneFor(map));
        foreach (var deckCard in run.Deck) _s.Deck.Add(deckCard.CloneFor(map));
        map.Finish();
        _s.Potions = run.Belt.ToArray();
        _s.Gold = Math.Max(0, run.Gold);
        _s.NextDeckCardId = run.NextDeckCardId;
    }

    /// <summary>
    /// Step 4 of the set-up in a run: the draw pile is one clone of each deck card in deck order,
    /// as the game clones the deck into the draw pile before it shuffles it.
    /// </summary>
    private void DrawPileFromDeck()
    {
        foreach (var deckCard in _s.Deck)
        {
            var card = deckCard.CombatCopy(_s.NextCardId++);
            card.Pile = PileType.Draw;
            _s.Draw.Add(card);
        }
    }

    /// <summary>
    /// What the fight hands back when it is over: copies of its relics and deck cards as they
    /// stand, the belt, the gold. HP and max HP are read off <see cref="Player"/>.
    /// </summary>
    internal RunHandOver HandBack()
    {
        var map = new CloneMap();
        var relics = new List<Relic>(_s.Relics.Count);
        foreach (var relic in _s.Relics) relics.Add(relic.CloneFor(map));
        var deck = new List<CardInstance>(_s.Deck.Count);
        foreach (var deckCard in _s.Deck) deck.Add(deckCard.CloneFor(map));
        map.Finish();
        return new RunHandOver(deck, relics, _s.Potions.ToArray(), _s.Gold, _s.NextDeckCardId);
    }
}
