using System.Reflection;

namespace SpireMind.Arena;

public sealed partial class Fight
{
    private readonly Stack<List<Listener>> _listenerPool = new();

    /// <summary>
    /// Set once a relic has left the player's relics during this fight (it was replaced or taken
    /// away): from then on a relic is looked up in the list before it is called. Only ever
    /// raised, so a fight put back to an earlier copy of its state merely looks up once too often.
    /// </summary>
    private bool _relicsLeft;

    /// <summary>
    /// Everyone who hears hooks, in the game's order (R-turns R2): in a run first the deck cards
    /// that listen as deck cards (<see cref="IListensInDeck"/>), in deck order; then the player's
    /// powers in the order they were first applied, then relics, then the hook objects of the
    /// cards in hand, draw, discard, exhaust and play piles; then for each enemy in list order
    /// its powers and the monster itself. (Potions in the belt come after the relics.) The list
    /// is a snapshot: who joins during a dispatch is not called; each entry is tested again just
    /// before its call (<see cref="StillListening"/>).
    /// <para>
    /// The game puts the deck cards in front only for the hooks that belong to the run as well
    /// as to the fight (RunState.IterateHookListeners with the fight: the start and the end of
    /// a fight, damage, HP, death, card moves, potions) and leaves them out of the fight's own.
    /// Here they are in front always, and a class that listens in the deck may override none of
    /// the fight's own hooks (<see cref="DeckListening"/>), which comes to the same.
    /// </para>
    /// </summary>
    private List<Listener> RentListeners()
    {
        var list = _listenerPool.Count > 0 ? _listenerPool.Pop() : new List<Listener>(32);
        if (_s.PlayerHooksActive) AddDeckListeners(list);
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

    /// <summary>
    /// Everyone who hears a hook of the run that is not also the fight's (room entered, gold
    /// gained, a card added to the deck): the game's RunState.IterateHookListeners(null), which
    /// is the same list whether or not a fight is on. Listening deck cards in deck order, relics
    /// in pickup order, potions in slot order; no power, no card in a pile, no monster.
    /// </summary>
    private List<Listener> RentRunListeners()
    {
        var list = _listenerPool.Count > 0 ? _listenerPool.Pop() : new List<Listener>(32);
        if (!_s.PlayerHooksActive) return list;
        AddDeckListeners(list);
        list.AddRange(_s.Relics);
        foreach (var potion in _s.Potions)
            if (potion?.Hooks is { } hooks) list.Add(hooks);
        return list;
    }

    private void AddDeckListeners(List<Listener> list)
    {
        // Empty in a single fight: only a run hands a fight its deck cards.
        foreach (var card in _s.Deck)
            if (card.Behaviour is IListensInDeck) list.Add(card.Behaviour);
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
    /// it left the fight; a deck card when it left the deck; a relic when it left the player.
    /// </summary>
    private bool StillListening(Listener l) => l switch
    {
        Power p => p.Owner.InCombat && (!p.Owner.IsPlayer || _s.PlayerHooksActive),
        Monster m => m.Creature.InCombat,
        CardBehaviour b => _s.PlayerHooksActive && b.Card.Pile switch
        {
            PileType.None => false,
            PileType.Deck => _s.Deck.Contains(b.Card),
            _ => true,
        },
        Relic r => _s.PlayerHooksActive && (!_relicsLeft || _s.Relics.Contains(r)),
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

    /// <summary>
    /// Calls <paramref name="call"/> on everyone who hears a hook of the run alone
    /// (<see cref="RentRunListeners"/>). Never guarded: these hooks do not belong to the fight,
    /// and the game sends them whatever state a fight is in.
    /// </summary>
    internal void EachOfRun(Action<Listener> call)
    {
        var list = RentRunListeners();
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

/// <summary>
/// Which hooks a card that listens as a deck card (<see cref="IListensInDeck"/>) may override,
/// and the check that every such card keeps to them.
/// <para>
/// In the game a hook is sent in one of three ways (Hooks/Hook.cs): to the run's listeners
/// alone (deck cards, relics, potions); to the run's deck cards and then the fight's listeners;
/// or to the fight's listeners alone. A deck card hears the first two. The arena asks a
/// listening deck card on every dispatch of a fight, so its class must leave the third kind
/// alone: then nothing reaches it that the game would not send.
/// </para>
/// </summary>
public static class DeckListening
{
    /// <summary>
    /// The fight hooks of <see cref="Listener"/> that the game also sends to the deck cards
    /// (RunState.IterateHookListeners with the fight), by name. Every hook of the run (the ones
    /// handed a <see cref="RunContext"/>) is heard by deck cards too.
    /// </summary>
    public static readonly IReadOnlySet<string> HeardByDeckCards = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(Listener.BeforeCombatStart), nameof(Listener.BeforeCombatStartLate),
        nameof(Listener.AfterCombatEnd), nameof(Listener.AfterCombatVictoryEarly), nameof(Listener.AfterCombatVictory),
        nameof(Listener.AfterCardChangedPiles), nameof(Listener.AfterCardChangedPilesLate),
        nameof(Listener.ModifyDamageAdditive), nameof(Listener.ModifyDamageMultiplicative), nameof(Listener.ModifyDamageCap),
        nameof(Listener.BeforeDamageReceived),
        nameof(Listener.ModifyHpLostBeforeOsty), nameof(Listener.ModifyHpLostBeforeOstyLate),
        nameof(Listener.ModifyHpLostAfterOsty), nameof(Listener.ModifyHpLostAfterOstyLate),
        nameof(Listener.AfterModifyingHpLostBeforeOsty), nameof(Listener.AfterModifyingHpLostAfterOsty),
        nameof(Listener.AfterCurrentHpChanged),
        nameof(Listener.AfterDamageReceived), nameof(Listener.AfterDamageReceivedLate),
        nameof(Listener.BeforeDeath), nameof(Listener.ShouldDie), nameof(Listener.ShouldDieLate),
        nameof(Listener.AfterDeath), nameof(Listener.AfterPreventingDeath),
        nameof(Listener.BeforePotionUsed), nameof(Listener.AfterPotionUsed),
    };

    /// <summary>True for a hook a deck card hears: one of the run's, or one of <see cref="HeardByDeckCards"/>.</summary>
    public static bool IsHeardByDeckCards(MethodInfo hook) =>
        HeardByDeckCards.Contains(hook.Name) || hook.GetParameters().FirstOrDefault()?.ParameterType == typeof(RunContext);

    /// <summary>The hooks a listener class overrides that a deck card never hears in the game. Empty for a class that may listen in the deck.</summary>
    public static IReadOnlyList<string> ForbiddenOverrides(Type behaviour)
    {
        var names = new List<string>();
        foreach (var method in behaviour.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!method.IsVirtual || method.GetBaseDefinition().DeclaringType != typeof(Listener)) continue;
            if (method.DeclaringType == typeof(Listener) || method.Name == nameof(Listener.OnCloned)) continue;
            if (!IsHeardByDeckCards(method)) names.Add(method.Name);
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <summary>Every registered card whose hook class listens in the deck and overrides a hook of
    /// the fight alone, one line each. The self-check fails on any.</summary>
    public static IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        foreach (var def in Content.Registry.Cards.Values.OrderBy(d => d.ClassName, StringComparer.Ordinal))
        {
            if (def.Behaviour is not { } make) continue;
            var type = make().GetType();
            if (!typeof(IListensInDeck).IsAssignableFrom(type)) continue;
            var forbidden = ForbiddenOverrides(type);
            if (forbidden.Count > 0)
                problems.Add($"{def.ClassName}: its hook class {type.Name} listens in the deck and overrides {string.Join(", ", forbidden)}, which the game does not send to deck cards");
        }
        return problems;
    }
}
