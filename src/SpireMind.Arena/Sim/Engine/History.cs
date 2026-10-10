namespace SpireMind.Arena;

public enum HistoryKind
{
    CardPlayStarted, CardPlayFinished, CardDrawn, CardExhausted, CardDiscarded, CardGenerated,
    DamageReceived, BlockGained, EnergySpent, PowerReceived, MonsterPerformedMove, PotionUsed, CreatureAttacked,
}

/// <summary>
/// One thing that happened in the fight, stamped with the round, side and player turn it
/// happened in. Which fields mean something depends on <see cref="Kind"/>.
/// </summary>
public readonly record struct HistoryEntry
{
    public HistoryKind Kind { get; init; }
    public int Round { get; init; }
    public Side Side { get; init; }
    public int Turn { get; init; }

    /// <summary>Creature id of the receiver (damage, block, power), the monster (move) or the attacker (attack); 0 is the player.</summary>
    public int ActorId { get; init; }

    /// <summary>Creature id of the dealer or applier, -1 for none.</summary>
    public int OtherId { get; init; }

    /// <summary>The card of a card entry; the card source of a damage entry.</summary>
    public CardDef? Card { get; init; }
    public int CardId { get; init; }

    /// <summary>The run of a card effect the entry belongs to (0 for none): <see cref="CardPlay.PlayId"/>.</summary>
    public int PlayId { get; init; }

    public bool IsAutoPlay { get; init; }

    /// <summary>Unblocked damage (HP actually lost), Block gained (whole number), energy spent, or a power's amount.</summary>
    public int Amount { get; init; }

    public int Blocked { get; init; }
    public bool WasKilled { get; init; }
    public ValueProp Props { get; init; }

    /// <summary>Power class name, move id or potion class name.</summary>
    public string? Name { get; init; }

    public bool FromHandDraw { get; init; }
    public bool CausedByEthereal { get; init; }
}

/// <summary>
/// The fight's log, for the cards and powers that ask "did X happen this turn". Entries are
/// recorded at the same points as in the game; "this turn" means the same round, side and
/// player turn number as now.
/// </summary>
public sealed class History
{
    private const int ChunkSize = 64;

    // Append-only, so copies of a fight share every full chunk and only copy the last, partial one.
    private readonly List<HistoryEntry[]> _full;
    private HistoryEntry[] _tail;
    private int _tailCount;

    internal History()
    {
        _full = new List<HistoryEntry[]>();
        _tail = new HistoryEntry[ChunkSize];
    }

    private History(History o)
    {
        _full = new List<HistoryEntry[]>(o._full);
        _tail = (HistoryEntry[])o._tail.Clone();
        _tailCount = o._tailCount;
    }

    internal History Clone() => new(this);

    public int EntryCount => _full.Count * ChunkSize + _tailCount;

    /// <summary>Every entry, oldest first.</summary>
    public IEnumerable<HistoryEntry> Entries
    {
        get
        {
            foreach (var chunk in _full)
                foreach (var e in chunk) yield return e;
            for (var i = 0; i < _tailCount; i++) yield return _tail[i];
        }
    }

    internal void Clear()
    {
        _full.Clear();
        _tailCount = 0;
    }

    private void Add(Fight f, HistoryEntry e)
    {
        if (_tailCount == ChunkSize)
        {
            _full.Add(_tail);
            _tail = new HistoryEntry[ChunkSize];
            _tailCount = 0;
        }
        var stamped = e with { Round = f.Round, Side = f.CurrentSide, Turn = f.Turn };
        _tail[_tailCount++] = stamped;
        f.HistoryTraced?.Invoke(stamped);
    }

    /// <summary>The entry happened in the turn that is running now.</summary>
    public static bool ThisTurn(Fight f, in HistoryEntry e) => e.Round == f.Round && e.Side == f.CurrentSide && e.Turn == f.Turn;

    public int Count(Func<HistoryEntry, bool> match)
    {
        var n = 0;
        foreach (var chunk in _full)
            foreach (var e in chunk)
                if (match(e)) n++;
        for (var i = 0; i < _tailCount; i++)
            if (match(_tail[i])) n++;
        return n;
    }

    public bool Any(Func<HistoryEntry, bool> match)
    {
        foreach (var chunk in _full)
            foreach (var e in chunk)
                if (match(e)) return true;
        for (var i = 0; i < _tailCount; i++)
            if (match(_tail[i])) return true;
        return false;
    }

    /// <summary>Counts this turn's entries that match. Entries are in time order, so the walk goes
    /// backwards and stops at the first entry of an earlier turn.</summary>
    public int CountThisTurn(Fight f, Func<HistoryEntry, bool> match)
    {
        var n = 0;
        for (var i = _tailCount - 1; i >= 0; i--)
        {
            if (!ThisTurn(f, _tail[i])) return n;
            if (match(_tail[i])) n++;
        }
        for (var c = _full.Count - 1; c >= 0; c--)
        {
            var chunk = _full[c];
            for (var i = ChunkSize - 1; i >= 0; i--)
            {
                if (!ThisTurn(f, chunk[i])) return n;
                if (match(chunk[i])) n++;
            }
        }
        return n;
    }

    // ---------- the questions in-scope content asks ----------

    /// <summary>Runs of card effects of <paramref name="type"/> started this turn (a replayed card counts each run; auto-plays count).</summary>
    public int CardPlaysStartedThisTurn(Fight f, CardType type) =>
        CountThisTurn(f, e => e.Kind == HistoryKind.CardPlayStarted && e.Card!.Type == type);

    /// <summary>Runs of card effects of <paramref name="type"/> finished this turn.</summary>
    public int CardPlaysFinishedThisTurn(Fight f, CardType type) =>
        CountThisTurn(f, e => e.Kind == HistoryKind.CardPlayFinished && e.Card!.Type == type);

    /// <summary>Runs of any card effect finished this turn.</summary>
    public int CardPlaysFinishedThisTurn(Fight f) => CountThisTurn(f, e => e.Kind == HistoryKind.CardPlayFinished);

    /// <summary>Runs of any card effect started this turn.</summary>
    public int CardPlaysStartedThisTurn(Fight f) => CountThisTurn(f, e => e.Kind == HistoryKind.CardPlayStarted);

    /// <summary>A card of the player was exhausted earlier this turn, for any reason.</summary>
    public bool WasCardExhaustedThisTurn(Fight f) => CardsExhaustedThisTurn(f) > 0;

    public int CardsExhaustedThisTurn(Fight f) => CountThisTurn(f, e => e.Kind == HistoryKind.CardExhausted);

    /// <summary>Damage events this turn in which <paramref name="creature"/> actually lost HP.</summary>
    public int HpLossEventsThisTurn(Fight f, Creature creature) =>
        CountThisTurn(f, e => e.Kind == HistoryKind.DamageReceived && e.ActorId == creature.Id && e.Amount > 0);

    /// <summary>Damage events in the whole fight in which <paramref name="creature"/> actually lost HP.</summary>
    public int HpLossEvents(Creature creature) =>
        Count(e => e.Kind == HistoryKind.DamageReceived && e.ActorId == creature.Id && e.Amount > 0);

    /// <summary>Card-or-move Block gains of <paramref name="creature"/> this turn that belong to another run of a
    /// card effect than <paramref name="except"/> (what Unmovable counts).</summary>
    public int CardBlockGainsThisTurn(Fight f, Creature creature, CardPlay? except) =>
        CountThisTurn(f, e => e.Kind == HistoryKind.BlockGained && e.ActorId == creature.Id && e.Props.IsCardOrMove()
                              && e.PlayId != (except?.PlayId ?? 0));

    // ---------- recording (engine only) ----------

    internal void CardPlayStarted(Fight f, CardPlay p) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.CardPlayStarted, Card = p.Card.Def, CardId = p.Card.Id, PlayId = p.PlayId, IsAutoPlay = p.IsAutoPlay, OtherId = p.Target?.Id ?? -1 });

    internal void CardPlayFinished(Fight f, CardPlay p) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.CardPlayFinished, Card = p.Card.Def, CardId = p.Card.Id, PlayId = p.PlayId, IsAutoPlay = p.IsAutoPlay, OtherId = p.Target?.Id ?? -1 });

    internal void CardDrawn(Fight f, CardInstance c, bool fromHandDraw) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.CardDrawn, Card = c.Def, CardId = c.Id, FromHandDraw = fromHandDraw });

    internal void CardExhausted(Fight f, CardInstance c, bool ethereal) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.CardExhausted, Card = c.Def, CardId = c.Id, CausedByEthereal = ethereal });

    internal void CardDiscarded(Fight f, CardInstance c) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.CardDiscarded, Card = c.Def, CardId = c.Id });

    internal void CardGenerated(Fight f, CardInstance c, Creature? creator) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.CardGenerated, Card = c.Def, CardId = c.Id, OtherId = creator?.Id ?? -1 });

    internal void DamageReceived(Fight f, Creature receiver, Creature? dealer, DamageResult r, CardInstance? source) =>
        Add(f, new HistoryEntry
        {
            Kind = HistoryKind.DamageReceived, ActorId = receiver.Id, OtherId = dealer?.Id ?? -1, Amount = r.UnblockedDamage,
            Blocked = r.BlockedDamage, WasKilled = r.WasTargetKilled, Props = r.Props, Card = source?.Def, CardId = source?.Id ?? 0,
        });

    internal void BlockGained(Fight f, Creature receiver, int amount, ValueProp props, CardPlay? play) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.BlockGained, ActorId = receiver.Id, Amount = amount, Props = props, PlayId = play?.PlayId ?? 0, Card = play?.Card.Def, CardId = play?.Card.Id ?? 0 });

    internal void EnergySpent(Fight f, int amount) => Add(f, new HistoryEntry { Kind = HistoryKind.EnergySpent, Amount = amount });

    internal void PowerReceived(Fight f, Power p, decimal amount, Creature? applier) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.PowerReceived, ActorId = p.Owner.Id, OtherId = applier?.Id ?? -1, Amount = (int)amount, Name = p.ClassName });

    internal void MonsterPerformedMove(Fight f, Creature monster, string moveId) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.MonsterPerformedMove, ActorId = monster.Id, Name = moveId });

    internal void PotionUsed(Fight f, PotionDef potion, Creature? target) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.PotionUsed, Name = potion.ClassName, OtherId = target?.Id ?? -1 });

    internal void CreatureAttacked(Fight f, Creature attacker, int hits) =>
        Add(f, new HistoryEntry { Kind = HistoryKind.CreatureAttacked, ActorId = attacker.Id, Amount = hits });
}
