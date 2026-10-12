namespace SpireMind.Arena;

// The deck changed from inside a fight.
//
// A run hands a fight its deck cards (Fight.Run.cs) and takes back whatever is in the fight's
// deck list when the fight ends. The deck commands of a run (Sim/Run/DeckEdits.cs) are for
// outside a fight; this is the one of them that a card of act 1 needs inside one: Guilty takes
// itself out of the deck from its own "after combat end" hook once it has seen five fights.
public sealed partial class Fight
{
    /// <summary>
    /// Removes a deck card from the deck from inside a fight (the game's
    /// CardPileCmd.RemoveFromDeck, which is the same command in a fight and outside one): "before
    /// card removed" is heard while the card is still in the deck, by the run's listeners alone
    /// (the deck cards that listen, the relics, the potions; no power and no card in a pile), then
    /// the card leaves the deck and is in no pile. Its hook object hears nothing from here on, not
    /// even the rest of a dispatch that is under way. The cards the fight made from it stay in
    /// their piles until the fight is over; <see cref="DeckVersionOf"/> gives null for them now.
    /// <para>
    /// Whether a card may be removed (<see cref="DeckEdits.IsRemovable"/>: not Eternal) is asked
    /// by whoever offers the choice, not by the command, as in the game.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The card is not in the fight's deck (the game throws the same).</exception>
    public void RemoveFromDeck(CardInstance deckCard)
    {
        if (deckCard.Pile != PileType.Deck || !_s.Deck.Contains(deckCard))
            throw new InvalidOperationException($"{deckCard} is not in the deck, so it cannot be removed from it.");

        var context = Context;
        EachOfRun(l => (l as IDeckListener)?.BeforeCardRemoved(context, deckCard));

        // A listener of that hook may itself have taken the card out.
        if (_s.Deck.Remove(deckCard)) deckCard.Pile = PileType.None;
    }
}
