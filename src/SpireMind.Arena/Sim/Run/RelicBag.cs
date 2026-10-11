namespace SpireMind.Arena;

/// <summary>
/// A relic bag of a run, as the game keeps it: one list of relic class names per rarity, each
/// shuffled once when the run starts. Relics are pulled from the front (elites, events, chests)
/// or from the back (shops), and a relic that is pulled is gone for the run. A run has two: the
/// shared bag (the whole shared pool; chests pull from it; it may refill a rarity that ran
/// empty) and the player's bag (shared pool plus the character's own, four rarities only).
/// <para>
/// This class is the bag alone. The rarity roll, the rule that a relic pulled from the
/// player's bag also leaves the shared bag, the shop's filter and what happens when a pull
/// lands on a relic the arena has left out belong to whoever pulls.
/// </para>
/// <para>Left out: the multiplayer side list that holds relics other players saw at a shared chest.</para>
/// </summary>
public sealed class RelicBag
{
    /// <summary>The rarities a player's bag holds, in the order the game asks them when it only wants to know whether anything is left.</summary>
    public static readonly IReadOnlyList<RelicRarity> PlayerRarities = [RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop];

    // The game keeps the lists in a dictionary that is only ever added to, so they stand in the
    // order their rarities first appeared in the pool. That order is the order of the shuffles.
    private readonly List<(RelicRarity Rarity, List<string> Relics)> _lists = new();
    private readonly bool _refreshAllowed;
    private List<string>? _original;

    /// <param name="refreshAllowed">True for the shared bag: a rarity that ran empty is filled again
    /// from the start list, in the pool's order and unshuffled, the next time it is asked for.</param>
    public RelicBag(bool refreshAllowed = false) => _refreshAllowed = refreshAllowed;

    private RelicBag(RelicBag other)
    {
        _refreshAllowed = other._refreshAllowed;
        _original = other._original?.ToList();
        foreach (var (rarity, relics) in other._lists) _lists.Add((rarity, relics.ToList()));
    }

    public RelicBag Clone() => new(this);

    public bool IsPopulated => _lists.Count > 0;

    /// <summary>The rarities that have a list, in the bag's own order (the order a save writes them in).</summary>
    public IReadOnlyList<RelicRarity> Rarities => _lists.Select(l => l.Rarity).ToList();

    /// <summary>The relics of a rarity as they stand, front first. Empty when the bag has no such list.</summary>
    public IReadOnlyList<string> Relics(RelicRarity rarity) => Find(rarity) ?? (IReadOnlyList<string>)[];

    /// <summary>The relics a player's bag is filled from: the unlocked shared pool, then the unlocked
    /// relics of the character's pool, without those of other rarities than the four of a bag.</summary>
    public static List<string> PlayerPool(RunUnlocks unlocks, string character)
    {
        if (!GameLists.CharacterRelics.TryGetValue(character, out var own))
            throw new ArgumentException($"'{character}' is not a character ({string.Join(", ", GameLists.CharacterRelics.Keys)}).", nameof(character));
        return unlocks.Relics(GameLists.SharedRelics).Concat(unlocks.Relics(own))
            .Where(r => PlayerRarities.Contains(GameLists.BagRelic(r).Rarity)).ToList();
    }

    /// <summary>
    /// Fills the bag: every relic goes to the list of its rarity, in the order given, and then
    /// each list is shuffled, in the order the lists were made. A list of one relic is shuffled
    /// without a draw.
    /// </summary>
    /// <exception cref="InvalidOperationException">The bag was filled before.</exception>
    public void Populate(IEnumerable<string> relics, GameRng rng)
    {
        if (IsPopulated) throw new InvalidOperationException("The relic bag was already filled.");
        _original = relics.ToList();
        foreach (var relic in _original) ListFor(GameLists.BagRelic(relic).Rarity).Add(relic);
        foreach (var (_, list) in _lists) rng.Shuffle(list);
    }

    /// <summary>The first relic of the rarity that passes <paramref name="filter"/>, taken out of the bag; null when no list has one.
    /// See <see cref="Available"/> for which list is searched.</summary>
    public string? PullFromFront(RelicRarity rarity, Func<string, bool>? isAllowed = null, Func<string, bool>? filter = null)
    {
        filter ??= _ => true;
        var list = Available(rarity, isAllowed, filter);
        if (list is null) return null;
        for (var i = 0; i < list.Count; i++)
        {
            if (!filter(list[i])) continue;
            var relic = list[i];
            list.RemoveAt(i);
            return relic;
        }
        return null;
    }

    /// <summary>The same from the back of the list: what a shop does.</summary>
    public string? PullFromBack(RelicRarity rarity, Func<string, bool>? isAllowed = null, Func<string, bool>? filter = null)
    {
        filter ??= _ => true;
        var list = Available(rarity, isAllowed, filter);
        if (list is null) return null;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!filter(list[i])) continue;
            var relic = list[i];
            list.RemoveAt(i);
            return relic;
        }
        return null;
    }

    /// <summary>Takes a relic out of every list, wherever it is. Nothing happens when it is not there.</summary>
    public void Remove(string relic)
    {
        foreach (var (_, list) in _lists) list.RemoveAll(r => r == relic);
    }

    public bool Contains(string relic) => _lists.Any(l => l.Relics.Contains(relic));

    /// <summary>Whether a pull of any of the four bag rarities would find a relic. Like a pull, asking
    /// drops the relics that are no longer allowed and may refill the shared bag.</summary>
    public bool HasAvailableRelics(Func<string, bool>? isAllowed = null) =>
        PlayerRarities.Any(rarity => Available(rarity, isAllowed, _ => true) is not null);

    // The list a pull of a rarity searches. First every relic the run no longer allows is
    // dropped from every list, for good (isAllowed: a relic's own condition, such as "not in
    // the last act"). An empty list of the asked rarity is refilled when the bag may refill.
    // Then, while the list holds nothing that passes the filter, the next rarity's list is
    // taken: Shop to Common to Uncommon to Rare, and after Rare (or from any other rarity)
    // nothing. Only the asked rarity is ever refilled.
    private List<string>? Available(RelicRarity rarity, Func<string, bool>? isAllowed, Func<string, bool> filter)
    {
        DropDisallowed(isAllowed);
        // As in the game: a rarity without a list gives an empty list that belongs to nobody, so
        // a refill that then makes the real list is not seen by this pull.
        var list = Find(rarity) ?? new List<string>();
        if (list.Count == 0 && _refreshAllowed)
        {
            Refresh(rarity);
            DropDisallowed(isAllowed);
        }
        while (list is not null && !list.Any(filter))
        {
            rarity = rarity switch
            {
                RelicRarity.Shop => RelicRarity.Common,
                RelicRarity.Common => RelicRarity.Uncommon,
                RelicRarity.Uncommon => RelicRarity.Rare,
                _ => RelicRarity.None,
            };
            list = rarity == RelicRarity.None ? null : Find(rarity) ?? new List<string>();
        }
        return list;
    }

    private void DropDisallowed(Func<string, bool>? isAllowed)
    {
        if (isAllowed is null) return;
        foreach (var (_, list) in _lists) list.RemoveAll(r => !isAllowed(r));
    }

    private void Refresh(RelicRarity rarity)
    {
        if (_original is null) throw new InvalidOperationException("A relic bag that was never filled cannot be refilled.");
        foreach (var relic in _original)
            if (GameLists.BagRelic(relic).Rarity == rarity) ListFor(rarity).Add(relic);
    }

    private List<string>? Find(RelicRarity rarity)
    {
        foreach (var (r, list) in _lists)
            if (r == rarity) return list;
        return null;
    }

    private List<string> ListFor(RelicRarity rarity)
    {
        if (Find(rarity) is { } list) return list;
        list = new List<string>();
        _lists.Add((rarity, list));
        return list;
    }
}
