using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>The room the run last entered outside a fight, as the hooks were told
/// (<see cref="Run.AfterRoomEntered"/>), and the floor it was on.</summary>
public sealed class EnteredRoom : RunPart
{
    public RoomKind Kind { get; internal set; }
    public int Floor { get; internal set; }

    public override RunPart Clone(CloneMap map) => new EnteredRoom { Kind = Kind, Floor = Floor };

    public override JsonNode? Dump() => new JsonObject { ["kind"] = Kind.ToString(), ["floor"] = Floor };
}

// The commands that change the player outside a fight as the game changes it: with the hooks
// each one sends, to the listeners the game sends them to, in its order. RunState's own
// setters and list methods stay what they are, bare; everything a room, an event or a relic
// does to the player goes through here (or, from a hook, through RunContext, which picks the
// fight's command inside a fight and these outside).
//
// Inside a fight none of these may be called: the fight works on its own copy of the player,
// and the run's state is the player as the fight began. Fight has the same commands.
//
// A player can die outside a fight (Damage, LoseMaxHp). The run is then over at once, and
// every command here does nothing from then on; whoever called the one that killed must test
// Run.IsOver and stop, or it would put its next screen over the game over screen.
public sealed partial class Run
{
    // The player outside a fight while one of the commands below runs (Fight.OutOfCombat).
    private Fight? _outside;

    /// <summary>What a hook of the run is handed outside a fight; <see cref="RunHooks"/> takes it too.</summary>
    public RunContext Context => new(this);

    // ---------- who hears a hook outside a fight ----------

    /// <summary>
    /// Everyone who hears a hook outside a fight, in the game's order (RunState.IterateHookListeners
    /// with no fight): the deck cards that listen as deck cards (<see cref="IListensInDeck"/>) in
    /// deck order, the relics in pickup order, the hook objects of the potions in slot order (one
    /// entry per slot, also when two slots hold the same potion). The list is taken when the first
    /// one is asked for; each entry is tested again just before it is handed out, so a relic that
    /// another one's hook removed is skipped, and nobody is handed out once the player is dead.
    /// This is the one definition of that order: every call site of a hook outside a fight walks
    /// it (<see cref="RunHooks"/> does, through <see cref="RunContext"/>).
    /// </summary>
    public IEnumerable<Listener> Listeners()
    {
        var state = _c.State;
        if (state.IsDead) yield break;
        var list = new List<Listener>(state.Relics.Count + 4);
        foreach (var card in state.Deck)
            if (card.Behaviour is IListensInDeck) list.Add(card.Behaviour);
        list.AddRange(state.Relics);
        foreach (var potion in state.Belt.Slots)
            if (potion?.Hooks is { } hooks) list.Add(hooks);

        foreach (var listener in list)
        {
            if (state.IsDead) yield break;
            var stillThere = listener switch
            {
                Relic relic => state.Relics.Contains(relic),
                CardBehaviour behaviour => behaviour.Card.Pile == PileType.Deck && state.Deck.Contains(behaviour.Card),
                _ => state.Belt.Slots.Any(potion => ReferenceEquals(potion?.Hooks, listener)),
            };
            if (stillThere) yield return listener;
        }
    }

    /// <summary>Calls <paramref name="call"/> on each of <see cref="Listeners"/> in turn.</summary>
    internal void EachListener(Action<Listener> call)
    {
        foreach (var listener in Listeners()) call(listener);
    }

    private RunState OutsideAFight(string command)
    {
        if (Fight is not null)
            throw new InvalidOperationException($"Run.{command} was called while a fight is on. Inside a fight the player is the fight's: use the fight's own command, or RunContext from a hook.");
        if (_outside is not null)
            throw new InvalidOperationException($"Run.{command} was called from inside Run.OutOfCombat: use the fight that body was handed.");
        return _c.State;
    }

    // ---------- HP ----------

    /// <summary>
    /// Runs <paramref name="body"/> on the player as it stands outside a fight: a
    /// <see cref="Fight"/> that is no combat (<see cref="Fight.IsCombat"/> false, never in
    /// progress, no enemies, no piles) with the run's HP, max HP, gold and belt and the run's own
    /// relic objects and deck cards. The commands of a fight that make sense without one work
    /// there as the game's do with no combat on: <see cref="Fight.Heal"/>,
    /// <see cref="Fight.GainMaxHp"/>, <see cref="Fight.LoseMaxHp"/>, <see cref="Fight.Damage(Creature, decimal, ValueProp, Creature?, CardInstance?)"/>,
    /// <see cref="Fight.Kill"/>, a potion's effect. The hooks they send reach deck cards, relics
    /// and potions, each with this fight as its first argument. Afterwards the run takes HP, max
    /// HP, gold, the belt and the lists back; a player who died there has ended the run
    /// (<see cref="RunResult.Died"/>, the game over screen), which the caller must test for
    /// (<see cref="IsOver"/>) before it goes on with its room.
    /// <para>
    /// Nothing may draw a random number from that fight's streams: they are nobody's. The body
    /// must not call a command of the run.
    /// </para>
    /// </summary>
    public void OutOfCombat(Action<Fight> body)
    {
        var state = OutsideAFight(nameof(OutOfCombat));
        if (state.IsDead) return;
        var outside = Arena.Fight.OutOfCombat(state.HandOver(), state.Hp, state.MaxHp, state.TotalFloor, state.MaxTurns);
        _outside = outside;
        try
        {
            body(outside);
        }
        finally
        {
            _outside = null;
        }
        if (outside.DrewRandomNumbers)
            throw new NotSupportedException("A hook drew a random number from a fight's streams while no fight was on. The arena has no rule for that: outside a fight a roll names its stream of the run (RunStreams.Draw).");
        state.TakeBack(outside.Player.Hp, outside.Player.MaxHp, outside.HandBackShared());
        if (state.IsDead) DieOutsideAFight();
    }

    /// <summary>
    /// Heals the player up to max HP (CreatureCmd.Heal). Outside a fight no hook hears a heal:
    /// the game sends "HP changed" for a heal only to a creature that is in a combat.
    /// </summary>
    public void Heal(decimal amount) => OutOfCombat(f => f.Heal(f.Player, amount));

    /// <summary>Raises max HP and heals by what it rose (CreatureCmd.GainMaxHp).</summary>
    public void GainMaxHp(decimal amount)
    {
        if (amount < 0m) throw new ArgumentException("The amount must not be negative; use LoseMaxHp.", nameof(amount));
        OutOfCombat(f => f.GainMaxHp(f.Player, amount));
    }

    /// <summary>
    /// Lowers max HP, never below 1 (CreatureCmd.LoseMaxHp). HP above the new maximum is first
    /// lost as damage that ignores Block and is not modified, through the damage hooks (Tungsten
    /// Rod takes 1 off it; the cut to the new maximum that follows makes the HP the same).
    /// </summary>
    public void LoseMaxHp(decimal amount, bool isFromCard = false)
    {
        if (amount < 0m) throw new ArgumentException("The amount must not be negative; use GainMaxHp.", nameof(amount));
        OutOfCombat(f => f.LoseMaxHp(f.Player, amount, isFromCard));
    }

    /// <summary>
    /// Damage to the player outside a fight, with no dealer and no card (CreatureCmd.Damage with
    /// no combat state): an event's "lose 6 HP" is <c>Damage(6)</c>. The whole chain runs with
    /// the run's listeners: the damage modifiers, "before damage received", the HP-loss
    /// modifiers (Tungsten Rod), "HP changed", "after damage received", and a death with the
    /// hooks that can prevent it (Lizard Tail, Fairy in a Bottle). Returns the HP lost. A player
    /// who dies has ended the run: test <see cref="IsOver"/> before going on.
    /// </summary>
    public int Damage(decimal amount, ValueProp props = Props.NonCardHpLoss)
    {
        var lost = 0;
        OutOfCombat(f => lost = f.Damage(f.Player, amount, props, null).UnblockedDamage);
        return lost;
    }

    /// <summary>The player died outside a fight (an event's price, a max HP loss): the run is over, as after a lost fight.</summary>
    private void DieOutsideAFight()
    {
        _c.Result = RunResult.Died;
        _c.Reason = null;
        _c.Screens.Clear();
        _c.Screens.Add(new GameOverScreen(Room.ToString()));
    }

    // ---------- gold ----------

    /// <summary>
    /// Gives the player gold (PlayerCmd.GainGold): the relics may change the amount (Bowler
    /// Hat), its whole-number part is paid, the listeners hear "gold gained" (Dragon Fruit).
    /// Nothing is paid when the amount ends at 0 or less. Returns what was paid.
    /// </summary>
    public int GainGold(decimal amount)
    {
        var state = OutsideAFight(nameof(GainGold));
        return state.IsDead ? 0 : RunHooks.GainGold(Context, amount);
    }

    /// <summary>Takes gold, never below 0 (PlayerCmd.LoseGold): a price paid, gold lost. No hook hears it.</summary>
    public void LoseGold(decimal amount)
    {
        var state = OutsideAFight(nameof(LoseGold));
        if (!state.IsDead) state.Gold = Math.Max(0, state.Gold - (int)amount);
    }

    // ---------- relics ----------

    /// <summary>
    /// Gives the player a relic (RelicCmd.Obtain), in the game's order: it joins the relics, at
    /// the end or at <paramref name="index"/>; every open screen that holds card rewards hears
    /// of it (<see cref="IHearsRelicObtained"/>); it leaves both relic bags; its pickup effect
    /// runs (<see cref="Relic.AfterObtained"/>), which may ask the player something or open a screen.
    /// </summary>
    public Relic ObtainRelic(Relic relic, int index = -1)
    {
        var state = OutsideAFight(nameof(ObtainRelic));
        if (state.Relics.Contains(relic)) throw new InvalidOperationException($"The player already holds this {relic.ClassName}.");
        if (state.IsDead) return relic;
        state.AddRelic(relic, index);
        foreach (var screen in _c.Screens.ToList())
            if (screen is IHearsRelicObtained pending) pending.OnRelicObtained(this, relic);
        RunRelics.TakeOutOfBags(state, relic.ClassName);
        relic.AfterObtained(Context);
        return relic;
    }

    /// <summary>The same for a relic of the registry, by class name ("Strawberry").</summary>
    /// <exception cref="KeyNotFoundException">The arena has not built that relic.</exception>
    public Relic ObtainRelic(string className, int index = -1) => ObtainRelic(Content.Registry.NewRelic(className), index);

    /// <summary>Takes a relic away from the player (RelicCmd.Remove) and tells it (<see cref="Relic.AfterRemoved"/>).</summary>
    public void RemoveRelic(Relic relic)
    {
        var state = OutsideAFight(nameof(RemoveRelic));
        if (state.IsDead) return;
        if (!state.RemoveRelic(relic)) throw new InvalidOperationException($"The player does not hold this {relic.ClassName}.");
        relic.AfterRemoved(Context);
    }

    /// <summary>Takes <paramref name="original"/> away and gives <paramref name="replacement"/> in its place in the relic list (RelicCmd.Replace).</summary>
    public Relic ReplaceRelic(Relic original, Relic replacement)
    {
        var state = OutsideAFight(nameof(ReplaceRelic));
        if (state.IsDead) return replacement;
        var index = -1;
        for (var i = 0; i < state.Relics.Count; i++)
            if (ReferenceEquals(state.Relics[i], original)) index = i;
        RemoveRelic(original);
        return ObtainRelic(replacement, index);
    }

    // ---------- rooms ----------

    /// <summary>
    /// The kind of room the run is in, for the game over screen of a death outside a fight: the
    /// room last entered on this floor, a fight's room after a fight on this floor, else Unassigned.
    /// </summary>
    public RoomKind Room
    {
        get
        {
            var state = _c.State;
            if (state.FindPart<EnteredRoom>() is { } entered && entered.Floor == state.TotalFloor && entered.Kind != RoomKind.Unassigned) return entered.Kind;
            if (_c.LastFight is { } fight && fight.Floor == state.TotalFloor) return RoomKinds.Of(fight.RoomType);
            return RoomKind.Unassigned;
        }
    }

    /// <summary>
    /// A room that is not a fight was entered and is set up: the relics hear "after room
    /// entered" (Meal Ticket heals in a shop, Eternal Feather at a rest site). Each room calls
    /// this at the moment the game does: a shop after its stock is made, a treasure room before
    /// its relic is rolled, a rest site after its options are made, an event after its start
    /// rolls and before its first page. A fight's room is not announced here: the fight sends
    /// the hook itself during its set-up, between the shuffle of the draw pile and the monsters'
    /// starting powers.
    /// </summary>
    public void AfterRoomEntered(RoomKind room)
    {
        var state = OutsideAFight(nameof(AfterRoomEntered));
        if (room.IsCombat())
            throw new ArgumentException($"A {room} room is a fight's: the fight tells the relics itself when it is set up (Run.StartFight).", nameof(room));
        if (state.IsDead) return;
        var entered = state.Part<EnteredRoom>();
        entered.Kind = room;
        entered.Floor = state.TotalFloor;
        RunHooks.AfterRoomEntered(Context, room);
    }
}
