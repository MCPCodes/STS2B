namespace SpireMind.Arena;

public sealed partial class Fight
{
    private readonly Stack<List<Listener>> _listenerPool = new();

    /// <summary>
    /// Everyone who hears hooks, in the game's order (R-turns R2): the player's powers in the
    /// order they were first applied, then relics, then the hook objects of the cards in hand,
    /// draw, discard, exhaust and play piles; then for each enemy in list order its powers and
    /// the monster itself. (Potions in the belt come after the relics.) The list is a snapshot: who joins during a dispatch is not called;
    /// each entry is tested again just before its call (<see cref="StillListening"/>).
    /// </summary>
    private List<Listener> RentListeners()
    {
        var list = _listenerPool.Count > 0 ? _listenerPool.Pop() : new List<Listener>(32);
        list.AddRange(_s.Player.PowerList);
        if (_s.PlayerHooksActive)
        {
            list.AddRange(_s.Relics);
            foreach (var potion in _s.Potions)
                if (potion?.Hooks is { } hooks) list.Add(hooks);
            AddBehaviours(list, _s.Hand);
            AddBehaviours(list, _s.Draw);
            AddBehaviours(list, _s.Discard);
            AddBehaviours(list, _s.Exhaust);
            AddBehaviours(list, _s.Play);
        }
        foreach (var enemy in _s.Enemies)
        {
            list.AddRange(enemy.PowerList);
            list.Add(enemy.Monster!);
        }
        return list;
    }

    private static void AddBehaviours(List<Listener> list, List<CardInstance> pile)
    {
        foreach (var card in pile)
            if (card.Behaviour is { } b) list.Add(b);
    }

    private void ReturnListeners(List<Listener> list)
    {
        list.Clear();
        _listenerPool.Push(list);
    }

    /// <summary>
    /// A power stays a listener after it was removed from a living owner (its owner reference is
    /// kept); it stops when its owner left the fight or, for the player, died. A card stops when
    /// it left the fight.
    /// </summary>
    private bool StillListening(Listener l) => l switch
    {
        Power p => p.Owner.InCombat && (!p.Owner.IsPlayer || _s.PlayerHooksActive),
        Monster m => m.Creature.InCombat,
        CardBehaviour b => b.Card.Pile != PileType.None && _s.PlayerHooksActive,
        Relic => _s.PlayerHooksActive,
        // A potion's hook object: only while that potion is still in the belt.
        _ => _s.PlayerHooksActive && _s.Potions.Any(p => ReferenceEquals(p?.Hooks, l)),
    };

    /// <summary>
    /// Calls <paramref name="call"/> on every listener. Guarded hooks reach nobody once the fight
    /// is over or ending (set-up excepted); the test is made once, when the dispatch starts, so a
    /// dispatch that began in a live fight finishes even if a listener kills the last enemy (R3).
    /// </summary>
    internal void Each(Action<Listener> call, bool guarded = true)
    {
        if (guarded && !_s.Starting && IsOverOrEnding) return;
        var list = RentListeners();
        foreach (var l in list)
            if (StillListening(l)) call(l);
        ReturnListeners(list);
    }

    /// <summary>True when every listener says yes.</summary>
    internal bool AllListeners(Func<Listener, bool> ask, bool guarded)
    {
        if (guarded && !_s.Starting && IsOverOrEnding) return true;
        var list = RentListeners();
        var all = true;
        foreach (var l in list)
        {
            if (!StillListening(l) || ask(l)) continue;
            all = false;
            break;
        }
        ReturnListeners(list);
        return all;
    }

    /// <summary>True when some listener says yes. Never guarded: it is what decides "ending".</summary>
    private bool AnyListener(Func<Listener, bool> ask)
    {
        var list = RentListeners();
        var any = false;
        foreach (var l in list)
        {
            if (!StillListening(l) || !ask(l)) continue;
            any = true;
            break;
        }
        ReturnListeners(list);
        return any;
    }

    /// <summary>The first listener, in hook order, for which <paramref name="match"/> holds. Never guarded.</summary>
    private Listener? FirstListener(Func<Listener, bool> match)
    {
        var list = RentListeners();
        Listener? found = null;
        foreach (var l in list)
        {
            if (!StillListening(l) || !match(l)) continue;
            found = l;
            break;
        }
        ReturnListeners(list);
        return found;
    }

    /// <summary>Runs an amount through every listener in turn.</summary>
    private decimal Fold(decimal amount, Func<Listener, decimal, decimal> step, bool guarded = true)
    {
        if (guarded && !_s.Starting && IsOverOrEnding) return amount;
        var list = RentListeners();
        foreach (var l in list)
            if (StillListening(l)) amount = step(l, amount);
        ReturnListeners(list);
        return amount;
    }
}
