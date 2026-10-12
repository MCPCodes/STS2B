using System.Collections;

namespace SpireMind.Arena;

/// <summary>
/// The picks of a card choice in a fight (<see cref="PendingChoice.Selected"/>), as indexes into
/// the choice's options, in the order the game's own screen holds them. That order is what the
/// snapshot lists under <c>selected</c>, what the confirm label names, and the order the effect
/// gets its cards in (Neow's Fury puts them into the hand in it).
/// <para>
/// The game's hand selection keeps a list: picks stay in the order they were made
/// (NPlayerHand). Its grid screens keep a .NET HashSet and hand that set back
/// (NCombatPileCardSelectScreen for a pile, NSimpleCardSelectScreen for a plain grid): a set
/// lists its items in the order they were added, except that a new item takes the place the
/// item removed last left free. So after picking A and B, taking A back and picking C, the game
/// has C before B. The order does not depend on hash values, only on the adds and removes.
/// </para>
/// </summary>
internal sealed class ChoicePicks : IEnumerable<int>
{
    // A grid screen's picks, in the set's order (the run-level screens use the same class); null for a hand selection.
    private readonly SelectionSet? _set;

    // A hand selection's picks, in the order made; null for a grid screen.
    private readonly List<int>? _list;

    /// <param name="asSet">True for the grid screens (a pile, a plain grid, the row of three), false for the hand.</param>
    public ChoicePicks(bool asSet)
    {
        if (asSet) _set = new SelectionSet();
        else _list = new List<int>();
    }

    private ChoicePicks(ChoicePicks other)
    {
        _set = other._set?.Clone();
        _list = other._list?.ToList();
    }

    public int Count => _set?.Count ?? _list!.Count;

    public bool Contains(int option) => _set?.Contains(option) ?? _list!.Contains(option);

    /// <summary>Picks an option. One that is already picked stays where it is.</summary>
    public void Add(int option)
    {
        if (_set is not null) _set.Add(option);
        else if (!_list!.Contains(option)) _list.Add(option);
    }

    /// <summary>Takes a pick back. False when the option was not picked.</summary>
    public bool Remove(int option) => _set?.Remove(option) ?? _list!.Remove(option);

    public ChoicePicks Clone() => new(this);

    public IEnumerator<int> GetEnumerator() => (_set?.InOrder() ?? _list!).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
