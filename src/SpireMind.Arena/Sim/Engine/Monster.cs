namespace SpireMind.Arena;

/// <summary>One thing a move announces. Attack intents carry the move's base damage and hit
/// count; the number shown is worked out live against the player (rules-damage.md R22).</summary>
public sealed class Intent
{
    private Intent(IntentType type) => Type = type;

    public IntentType Type { get; }

    /// <summary>Base damage per hit of an attack intent (before Strength and the rest); null for other intents.</summary>
    public Func<decimal>? Damage { get; private init; }

    /// <summary>Hit count of an attack intent.</summary>
    public Func<int>? Repeats { get; private init; }

    /// <summary>Card count of a status intent; the snapshot reads "statuscard: N".</summary>
    public int StatusCount { get; private init; }

    public bool IsAttack => Damage is not null;

    public static Intent Attack(decimal damage, int hits = 1) => new(IntentType.Attack) { Damage = () => damage, Repeats = () => hits };
    public static Intent Attack(Func<decimal> damage, Func<int>? hits = null) => new(IntentType.Attack) { Damage = damage, Repeats = hits ?? (() => 1) };
    public static Intent DeathBlow(decimal damage) => new(IntentType.DeathBlow) { Damage = () => damage, Repeats = () => 1 };
    public static Intent Status(int cards) => new(IntentType.StatusCard) { StatusCount = cards };

    public static Intent Buff { get; } = new(IntentType.Buff);
    public static Intent Debuff { get; } = new(IntentType.Debuff);
    public static Intent DebuffStrong { get; } = new(IntentType.DebuffStrong);
    public static Intent Defend { get; } = new(IntentType.Defend);
    public static Intent CardDebuff { get; } = new(IntentType.CardDebuff);
    public static Intent Sleep { get; } = new(IntentType.Sleep);
    public static Intent Summon { get; } = new(IntentType.Summon);
    public static Intent Heal { get; } = new(IntentType.Heal);
    public static Intent Stun { get; } = new(IntentType.Stun);
    public static Intent Escape { get; } = new(IntentType.Escape);
    public static Intent Hidden { get; } = new(IntentType.Hidden);
    public static Intent Unknown { get; } = new(IntentType.Unknown);
}

/// <summary>A state of a monster's move machine: a move, or a branch that leads to one.</summary>
public abstract class MonsterState
{
    protected MonsterState(string id) => Id = id;

    public string Id { get; }

    internal virtual bool IsMove => false;

    /// <summary>The id of the state that follows this one; empty means the machine's initial state.</summary>
    internal abstract string? NextStateId(Fight f, Monster owner);
}

/// <summary>A move: what the monster does on its turn, the intents it shows before, and what follows.</summary>
public sealed class MoveState : MonsterState
{
    internal MoveState(string id, Action<Fight> perform, IReadOnlyList<Intent> intents) : base(id)
    {
        Perform = perform;
        Intents = intents;
    }

    internal Action<Fight> Perform { get; }
    public IReadOnlyList<Intent> Intents { get; }

    /// <summary>The state after this move: another move or a branch. Set it for every move.</summary>
    public MonsterState? FollowUp { get; set; }

    /// <summary>The machine may not leave this move before it was performed once since it was entered.</summary>
    public bool MustPerformOnce { get; set; }

    internal override bool IsMove => true;

    internal override string? NextStateId(Fight f, Monster owner) =>
        FollowUp?.Id ?? throw new InvalidOperationException($"{owner.ClassName}: move {Id} has no follow-up.");

    /// <summary>Sets the follow-up and returns it, so a cycle reads <c>a.Then(b).Then(c).Then(a)</c>.</summary>
    public T Then<T>(T next) where T : MonsterState
    {
        FollowUp = next;
        return next;
    }
}

/// <summary>How often a random branch may pick the same move again.</summary>
public enum MoveRepeat
{
    /// <summary>No limit.</summary>
    Forever,

    /// <summary>Not right after itself.</summary>
    CannotRepeat,

    /// <summary>Once per fight.</summary>
    UseOnlyOnce,

    /// <summary>At most a given number of times in a row.</summary>
    MaxInARow,
}

/// <summary>
/// A weighted random choice among states. One number is drawn from the MonsterAi stream per
/// visit, also when only one option is open; an option whose repeat rule or cooldown forbids
/// it weighs 0; with every option at 0 the first listed one is taken (rules-turns.md R42).
/// </summary>
public sealed class RandomBranch : MonsterState
{
    private readonly record struct Option(string StateId, Func<float> Weight, MoveRepeat Repeat, int MaxTimes, int Cooldown);

    private readonly List<Option> _options = new();

    internal RandomBranch(string id) : base(id) { }

    /// <param name="state">The option.</param>
    /// <param name="weight">Its weight when it is allowed.</param>
    /// <param name="repeat">Its repeat rule.</param>
    /// <param name="maxInARow">For <see cref="MoveRepeat.MaxInARow"/>: how many times in a row it may be chosen.</param>
    /// <param name="cooldown">Forbidden while it is among the last this many chosen moves.</param>
    public RandomBranch Add(MonsterState state, float weight = 1f, MoveRepeat repeat = MoveRepeat.Forever, int maxInARow = 0, int cooldown = 0) =>
        Add(state, () => weight, repeat, maxInARow, cooldown);

    public RandomBranch Add(MonsterState state, Func<float> weight, MoveRepeat repeat = MoveRepeat.Forever, int maxInARow = 0, int cooldown = 0)
    {
        if (repeat == MoveRepeat.MaxInARow && maxInARow < 1)
            throw new ArgumentException("MaxInARow needs maxInARow of 1 or more.", nameof(maxInARow));
        _options.Add(new Option(state.Id, weight, repeat, maxInARow, cooldown));
        return this;
    }

    internal override string? NextStateId(Fight f, Monster owner)
    {
        var log = owner.MoveLog;
        // The game adds the weights up with the runtime's Sum over floats, which adds in double
        // and narrows to float once at the end; the walk below subtracts in float.
        var sum = 0d;
        foreach (var o in _options) sum += Weight(o, log);
        var total = (float)sum;
        var roll = f.Rng.MonsterAi.NextFloat(total);
        foreach (var o in _options)
        {
            roll -= Weight(o, log);
            if (roll <= 0f) return o.StateId;
        }
        throw new InvalidOperationException($"{owner.ClassName}: random branch {Id} found no state.");
    }

    private static float Weight(Option o, IReadOnlyList<string> log)
    {
        var allowed = true;
        switch (o.Repeat)
        {
            case MoveRepeat.UseOnlyOnce:
                allowed = !log.Contains(o.StateId);
                break;
            case MoveRepeat.CannotRepeat:
            case MoveRepeat.MaxInARow:
                // Forbidden exactly when the last n chosen moves were all this one.
                var n = o.Repeat == MoveRepeat.CannotRepeat ? 1 : o.MaxTimes;
                if (log.Count >= n)
                {
                    allowed = false;
                    for (var back = 0; back < n; back++)
                    {
                        if (log[log.Count - 1 - back] != o.StateId)
                        {
                            allowed = true;
                            break;
                        }
                    }
                }
                break;
        }

        if (o.Cooldown > 0)
        {
            for (var back = 0; back < o.Cooldown && back < log.Count; back++)
                if (log[log.Count - 1 - back] == o.StateId) return 0f;
        }
        return allowed ? o.Weight() : 0f;
    }
}

/// <summary>Takes the first listed option whose condition holds. No random number.</summary>
public sealed class ConditionalBranch : MonsterState
{
    private readonly List<(string StateId, Func<Fight, bool>? Condition)> _options = new();

    internal ConditionalBranch(string id) : base(id) { }

    public ConditionalBranch When(Func<bool> condition, MonsterState state) => When(_ => condition(), state);

    /// <summary>A condition that reads the fight as it is when the move is chosen (the start of a player turn).</summary>
    public ConditionalBranch When(Func<Fight, bool> condition, MonsterState state)
    {
        _options.Add((state.Id, condition));
        return this;
    }

    /// <summary>An option that always holds; list it last.</summary>
    public ConditionalBranch Otherwise(MonsterState state)
    {
        _options.Add((state.Id, null));
        return this;
    }

    internal override string? NextStateId(Fight f, Monster owner)
    {
        foreach (var (id, condition) in _options)
            if (condition is null || condition(f)) return id;
        throw new InvalidOperationException($"{owner.ClassName}: conditional branch {Id} found no state.");
    }
}

/// <summary>Collects the states of one monster's machine. Every state made here can be a follow-up.</summary>
public sealed class MachineBuilder
{
    internal Dictionary<string, MonsterState> States { get; } = new(StringComparer.Ordinal);

    public MoveState Move(string id, Action<Fight> perform, params Intent[] intents) => Register(new MoveState(id, perform, intents));

    public RandomBranch Random(string id) => Register(new RandomBranch(id));

    public ConditionalBranch Conditional(string id) => Register(new ConditionalBranch(id));

    private T Register<T>(T state) where T : MonsterState
    {
        States.Add(state.Id, state);
        return state;
    }
}

/// <summary>
/// A move that is not part of the monster's own machine and is put in from outside: the
/// engine's stun, Illusion's revive. It is plain data so that it survives a clone;
/// <see cref="Performer"/> gets <see cref="Listener"/>-less work done through
/// <see cref="Monster.PerformForcedMove"/> or, for a power, <see cref="IForcedMovePerformer"/>.
/// </summary>
public sealed record ForcedMove(string Id, IReadOnlyList<Intent> Intents, string? FollowUpId, bool MustPerformOnce, Listener? Performer = null);

/// <summary>Implemented by a power that forces a move onto its owner and wants to run when the move is performed.</summary>
public interface IForcedMovePerformer
{
    void PerformForcedMove(Fight f, Creature monster, string moveId);
}

/// <summary>
/// One monster: its numbers, its starting powers and its move machine. The class name is the
/// game's ("Nibbit"). Write the moves as instance methods taking the fight; keep any flags as
/// fields (they are copied when the fight is cloned).
/// </summary>
public abstract class Monster : Listener
{
    /// <summary>The id of the engine's stun state; the snapshot reads "stunned" for it.</summary>
    public const string StunnedId = "STUNNED";

    private Dictionary<string, MonsterState> _states = null!;
    private MonsterState _initial = null!;
    private MonsterState _current = null!;
    private MoveState? _forcedState;
    private ForcedMove? _forced;
    private bool _performedFirstMove;
    private bool _currentPerformed;
    private List<string> _log = new();

    public Creature Creature { get; internal set; } = null!;

    public string ClassName => GetType().Name;

    /// <summary>The game's English name.</summary>
    public abstract string Name { get; }

    /// <summary>Starting HP range at ascension 0, both ends included.</summary>
    public abstract int MinHp { get; }
    public abstract int MaxHp { get; }

    /// <summary>Starting powers. Runs when the monster joins the fight, before its first move is settled.
    /// At the start of a fight Block cannot be gained here (the fight is not in progress yet); powers can.</summary>
    public virtual void AfterAddedToRoom(Fight f) { }

    /// <summary>Builds the machine and returns its initial state (a move, or a branch that is resolved at once).</summary>
    protected abstract MonsterState BuildMachine(MachineBuilder m);

    /// <summary>Runs when a <see cref="ForcedMove"/> with no performer of its own is performed.</summary>
    public virtual void PerformForcedMove(Fight f, string moveId) { }

    // ---------- state the rest of the engine reads ----------

    /// <summary>The move the monster will perform on its next turn; null only before its first move was settled.</summary>
    public MoveState? NextMove => _current as MoveState;

    /// <summary>Every move chosen so far, oldest first (logged when chosen, not when performed).</summary>
    public IReadOnlyList<string> MoveLog => _log;

    /// <summary>Added during the current side's turn: it does not act this enemy turn.</summary>
    public bool SpawnedThisTurn { get; internal set; }

    public bool IsPerformingMove { get; internal set; }

    public bool IsStunned => _current.Id == StunnedId;

    public bool HasPerformedAMove => _performedFirstMove;

    // ---------- helpers for move bodies ----------

    /// <summary>A monster attack on the player: powered, <paramref name="hits"/> separate hits.</summary>
    protected AttackCommand Attack(Fight f, decimal damage, int hits = 1) =>
        f.Attack(Creature, damage).TargetingAllOpponents().Hits(hits).Execute();

    /// <summary>Block from a move (the monster's own Dexterity and Frail apply).</summary>
    protected decimal GainBlock(Fight f, decimal amount) => f.GainBlock(Creature, amount, Props.MonsterMove, null);

    protected T? ApplyToSelf<T>(Fight f, decimal amount) where T : Power, new() => f.ApplyPower<T>(Creature, amount, Creature);

    protected T? ApplyToPlayer<T>(Fight f, decimal amount) where T : Power, new() => f.ApplyPower<T>(f.Player, amount, Creature);

    /// <summary>Creates status cards one by one straight into one of the player's piles. Nobody is
    /// named as their creator: the game's monsters pass none (a creator there is always a player).</summary>
    protected void AddStatusCards(Fight f, string cardClassName, int count, PileType pile = PileType.Discard, PilePosition position = PilePosition.Bottom) =>
        f.AddStatusCards(cardClassName, count, pile, position, creator: null);

    // ---------- the machine (rules-turns.md R8, R42, R44) ----------

    internal void SetUpForCombat()
    {
        Rebuild();
        _current = _initial;
        if (_initial.IsMove) _log.Add(_initial.Id);
        SpawnedThisTurn = true;
    }

    private void Rebuild()
    {
        var builder = new MachineBuilder();
        _initial = BuildMachine(builder);
        _states = builder.States;
        if (!_states.ContainsKey(_initial.Id))
            throw new InvalidOperationException($"{ClassName}: the initial state {_initial.Id} was not made by the builder.");
    }

    private bool CanTransitionAway => _current is not MoveState { MustPerformOnce: true } || _currentPerformed;

    /// <summary>Chooses the next move, as at the start of a player turn. Does nothing before the
    /// monster's first performed move, or while a must-perform move is still waiting.</summary>
    internal void RollMove(Fight f)
    {
        if (!CanTransitionAway || (!_performedFirstMove && _current.IsMove)) return;

        string? toLog = null;
        do
        {
            var nextId = ReferenceEquals(_current, _forcedState) ? _forced!.FollowUpId : _current.NextStateId(f, this);
            MonsterState next;
            if (string.IsNullOrEmpty(nextId)) next = _initial;
            else if (!_states.TryGetValue(nextId, out next!))
                throw new InvalidOperationException($"{ClassName}: no state called {nextId}.");
            SetCurrent(next);
            toLog ??= next.IsMove ? next.Id : null;
        }
        while (!_current.IsMove);
        if (toLog is not null) _log.Add(toLog);
    }

    private void SetCurrent(MonsterState state)
    {
        _currentPerformed = false;
        if (!ReferenceEquals(state, _forcedState))
        {
            _forced = null;
            _forcedState = null;
        }
        _current = state;
    }

    /// <summary>Replaces the intended move at once with one of the machine's own states, by id.
    /// Nothing happens while a must-perform move is waiting, unless <paramref name="forceTransition"/>.</summary>
    public void SetMoveImmediate(string stateId, bool forceTransition = false)
    {
        if (!CanTransitionAway && !forceTransition) return;
        if (!_states.TryGetValue(stateId, out var state) || !state.IsMove)
            throw new InvalidOperationException($"{ClassName}: no move called {stateId}.");
        SetCurrent(state);
    }

    /// <summary>Replaces the intended move at once with a move from outside the machine.</summary>
    public void SetMoveImmediate(ForcedMove move, bool forceTransition = false)
    {
        if (!CanTransitionAway && !forceTransition) return;
        _forced = move;
        _forcedState = MakeForcedState(move);
        SetCurrent(_forcedState);
    }

    private MoveState MakeForcedState(ForcedMove move) =>
        new(move.Id, f => PerformForced(f, move), move.Intents) { MustPerformOnce = move.MustPerformOnce };

    private void PerformForced(Fight f, ForcedMove move)
    {
        if (move.Performer is IForcedMovePerformer performer) performer.PerformForcedMove(f, Creature, move.Id);
        else PerformForcedMove(f, move.Id);
    }

    /// <summary>The engine's stun: a do-nothing move that must be performed once; afterwards
    /// <paramref name="nextMoveId"/> follows, or by default the last move in the log.</summary>
    internal void Stun(string? nextMoveId)
    {
        if (string.IsNullOrEmpty(nextMoveId)) nextMoveId = _log.Count > 0 ? _log[^1] : null;
        SetMoveImmediate(new ForcedMove(StunnedId, [Intent.Stun], nextMoveId, MustPerformOnce: true));
    }

    internal void PerformMove(Fight f)
    {
        if (NextMove is not { } move) return;
        IsPerformingMove = true;
        _currentPerformed = true;
        move.Perform(f);
        _performedFirstMove = true;
        f.History.MonsterPerformedMove(f, Creature, move.Id);
        IsPerformingMove = false;
    }

    /// <summary>For scripted checks: makes <paramref name="stateId"/> the next move without the rules that normally guard it.</summary>
    public void ForceNextMove(string stateId) => SetMoveImmediate(stateId, forceTransition: true);

    internal Monster CloneFor(CloneMap map)
    {
        var copy = (Monster)MemberwiseClone();
        map.Add(this, copy);
        copy._log = new List<string>(_log);
        return copy;
    }

    /// <summary>The machine's delegates are bound to the monster they were built for, so a copy builds its own.</summary>
    internal void FinishClone(CloneMap map)
    {
        var currentId = _current.Id;
        var wasForced = _forced is not null && ReferenceEquals(_current, _forcedState);
        Rebuild();
        if (_forced is not null)
        {
            _forced = _forced with { Performer = map.Get(_forced.Performer) };
            _forcedState = MakeForcedState(_forced);
        }
        _current = wasForced ? _forcedState! : _states[currentId];
    }
}
