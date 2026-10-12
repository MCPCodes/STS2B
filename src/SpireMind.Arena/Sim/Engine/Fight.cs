namespace SpireMind.Arena;

/// <summary>
/// One fight, by the game's rules (the numbered rules of rules-turns.md and rules-damage.md
/// are cited as R-turns and R-damage where they are implemented). Content talks to this
/// class: it holds the state and the commands (damage, block, powers, cards, energy), and it
/// runs the turn loop. It is deterministic from its seed, and it answers the same action ids
/// and shows the same snapshots as the mod's combat reader.
/// <para>
/// The parts: Fight.Hooks (who hears a hook), Fight.Turns (the turn loop), Fight.Damage
/// (damage, attacks, block, healing, death), Fight.Powers, Fight.Cards (piles, drawing,
/// playing), Fight.Monsters, Fight.Choices (cards the player must pick), Fight.Actions (action
/// ids), Fight.Snapshot (what the model sees), Fight.Run (what a run hands a fight and takes
/// back), Fight.Relics (gold and relics gained inside a fight, and the player outside one).
/// </para>
/// </summary>
public sealed partial class Fight
{
    public const int HandLimit = 10;
    public const int BaseHandDraw = 5;

    /// <summary>The player's display name: the Ironclad's title in the game's English table.</summary>
    public const string PlayerName = "The Ironclad";

    private FightState _s;
    private readonly FightInfo _info;

    internal Fight(FightConfig config)
    {
        var registry = Content.Registry;
        Encounter? encounter = config.Monsters is null
            ? registry.NewEncounter(config.Encounter ?? throw new ArgumentException("A fight needs an encounter or monsters."))
            : null;
        _info = new FightInfo(
            config.Run is { } run ? run.Deck.Select(c => (c.Def, c.IsUpgraded)).ToArray() : config.Deck.ToArray(),
            encounter?.Slots ?? config.Slots,
            encounter?.RoomType ?? config.RoomType,
            encounter?.ClassName ?? config.Encounter ?? "Custom",
            config.Floor, config.Seed, config.MaxTurns);
        _s = new FightState { Rng = config.Rng ?? new RngSet(config.Seed) };
        StartCombat(config, encounter);
    }

    private Fight(Fight other)
    {
        _info = other._info;
        _s = other._s.Clone(out var map);
        _checkpoint = other._checkpoint;
        _pendingAction = other._pendingAction;
        _answers = other._answers.ToList();
        _pending = other._pending?.CloneFor(map);
        _rerandomize = other._rerandomize.ToList();
        _scripted = other._scripted;
        _relicsLeft = other._relicsLeft;
        _outOfCombat = other._outOfCombat;
    }

    // ---------- what content reads ----------

    public Creature Player => _s.Player;

    /// <summary>The live enemy list in acting order. A dead monster has left it, unless a power keeps it (Illusion).</summary>
    public IReadOnlyList<Creature> Enemies => _s.Enemies;

    /// <summary>Every enemy the fight has had, at the index of its "e&lt;j&gt;" id.</summary>
    public IReadOnlyList<Creature> Roster => _s.Roster;

    public IReadOnlyList<CardInstance> Hand => _s.Hand;

    /// <summary>Index 0 is the top: the next card drawn.</summary>
    public IReadOnlyList<CardInstance> DrawPile => _s.Draw;
    public IReadOnlyList<CardInstance> DiscardPile => _s.Discard;
    public IReadOnlyList<CardInstance> ExhaustPile => _s.Exhaust;

    /// <summary>Cards that are resolving right now.</summary>
    public IReadOnlyList<CardInstance> PlayPile => _s.Play;

    /// <summary>Every card in a combat pile: hand, draw, discard, exhaust, play, in that order.</summary>
    public IEnumerable<CardInstance> AllCards => _s.Hand.Concat(_s.Draw).Concat(_s.Discard).Concat(_s.Exhaust).Concat(_s.Play);

    public IReadOnlyList<CardInstance> PileOf(PileType pile) => PileList(pile);

    public IReadOnlyList<Relic> Relics => _s.Relics;

    /// <summary>The belt: three slots, null when empty.</summary>
    public IReadOnlyList<PotionDef?> Potions => _s.Potions;

    public int Energy => _s.Energy;

    /// <summary>The player's turn number, from 1. The snapshot's "turn".</summary>
    public int Turn => _s.Turn;

    /// <summary>The fight's round number, from 1. Equal to <see cref="Turn"/> in single player.</summary>
    public int Round => _s.Round;

    public Side CurrentSide => _s.CurrentSide;
    public TurnPhase Phase => _s.Phase;
    public RngSet Rng => _s.Rng;
    public History History => _s.History;

    public RoomType RoomType => _info.RoomType;
    public string EncounterName => _info.EncounterName;
    public int Floor => _info.Floor;
    public int Seed => _info.Seed;
    /// <summary>The deck the fight started with, in deck order.</summary>
    public IReadOnlyList<(CardDef Def, bool Upgraded)> Deck => _info.Deck;

    // ---------- the state of the fight (R-turns R3) ----------

    /// <summary>True from the first turn until the fight is won or lost.</summary>
    public bool InProgress => _s.InProgress;

    /// <summary>The fight is being set up: monsters are getting their starting powers.</summary>
    public bool IsStarting => _s.Starting;

    /// <summary>
    /// The fight is running but already decided: the player's death is pending, or no primary
    /// enemy is alive and nothing keeps the fight going. Most commands do nothing from here on;
    /// the end is processed at the next win check.
    /// </summary>
    public bool IsEnding
    {
        get
        {
            if (!_s.InProgress) return false;
            if (_s.PendingLoss) return true;
            foreach (var e in _s.Enemies)
                if (e.IsAlive && e.IsPrimaryEnemy) return false;
            return !AnyListener(l => l.ShouldStopCombatFromEnding(this));
        }
    }

    /// <summary>Ending, or not in progress at all (which includes the set-up before turn 1).</summary>
    public bool IsOverOrEnding => !_s.InProgress || IsEnding;

    /// <summary>The fight has been decided and processed.</summary>
    public bool IsOver => _s.Over;
    public bool Won => _s.Won;

    /// <summary>Lost by the arena's turn limit, not by dying.</summary>
    public bool TimedOut => _s.TimedOut;

    public int CardsPlayed => _s.CardsPlayed;

    /// <summary>All HP the player lost in the fight, whatever took it.</summary>
    public int DamageTaken => _s.DamageTaken;

    /// <summary>What Burning Blood gave back after a win.</summary>
    public int VictoryHeal => _s.VictoryHeal;

    /// <summary>HP of the living enemies still in the fight; 0 after a win.</summary>
    public int EnemyHpLeft => _s.Enemies.Where(e => e.IsAlive).Sum(e => e.Hp);

    public int MaxTurns => _info.MaxTurns;

    // ---------- creature lists ----------

    public IReadOnlyList<Creature> CreaturesOnSide(Side side) => side == Side.Player ? [_s.Player] : _s.Enemies.ToList();

    public IReadOnlyList<Creature> OpponentsOf(Creature creature) => CreaturesOnSide(creature.Side.Opposite());

    /// <summary>Alive, and no power says it cannot be hit (a reviving Eye with Teeth).</summary>
    public bool IsHittable(Creature creature)
    {
        if (creature.IsDead) return false;
        return AllListeners(l => l.ShouldAllowHitting(this, creature), guarded: true);
    }

    /// <summary>In the fight and not shielded from powers by a hook. Being dead does not by itself stop powers.</summary>
    public bool CanReceivePowers(Creature creature) =>
        creature.InCombat && AllListeners(l => l.ShouldAllowHitting(this, creature), guarded: true);

    public IReadOnlyList<Creature> HittableEnemies => _s.Enemies.Where(IsHittable).ToList();

    /// <summary>A living enemy still in the fight (what the snapshot lists).</summary>
    public bool InFight(Creature enemy) => enemy.IsAlive && _s.Enemies.Contains(enemy);

    /// <summary>What a card or potion may be aimed at: in the fight, hittable, and no hook forbids targeting.</summary>
    public bool IsTargetableEnemy(Creature enemy) =>
        InFight(enemy) && IsHittable(enemy) && AllListeners(l => l.ShouldAllowTargeting(this, enemy), guarded: true);

    // ---------- start of combat (R-turns R5 to R9) ----------

    private void StartCombat(FightConfig config, Encounter? encounter)
    {
        var registry = Content.Registry;
        _s.Starting = true;

        // 1. The player is creature 0. Its name is the character's title, as Creature.Name gives it
        // in single player: the game's English text for it is "The Ironclad".
        _s.Player = new Creature(_s.NextCreatureId++, Side.Player, PlayerName, Math.Min(config.Hp, config.MaxHp), config.MaxHp, null, null);
        foreach (var relic in config.Relics) _s.Relics.Add(registry.NewRelic(relic));
        _s.Relics.AddRange(config.ExtraRelics);
        for (var i = 0; i < _s.Potions.Length && i < config.Potions.Length; i++)
            _s.Potions[i] = config.Potions[i] is { } name ? registry.Potion(name) : null;
        // In a run the relics, the belt, the gold and the deck are the run's instead (Fight.Run.cs).
        if (config.Run is not null) TakeFromRun(config.Run);

        // 2 and 3. The line-up, created one by one; each HP roll avoids the values already taken.
        var lineUp = config.Monsters ?? encounter!.GenerateMonsters(_s.Rng.Encounter).Select(m => new PlacedMonster(m.Monster, m.Slot)).ToList();
        foreach (var placed in lineUp) CreateCreature(placed.Monster, placed.Slot, placed.Hp);

        // 4. The draw pile is the deck in deck order, shuffled once without sorting first (R7).
        foreach (var entry in config.Run is null ? config.Deck : [])
        {
            var card = NewCard(entry.Def, entry.Upgraded);
            card.Pile = PileType.Draw;
            _s.Draw.Add(card);
        }
        if (config.Run is not null) DrawPileFromDeck();
        if (config.ShuffleDeck) _s.Rng.Shuffle.Shuffle(_s.Draw);

        foreach (var enemy in _s.Enemies.ToList())
        {
            enemy.Monster!.SetUpForCombat();
            if (enemy.Slot is not null) SortEnemiesBySlot();
        }
        foreach (var enemy in _s.Enemies) AddToRoster(enemy);

        // 5. The room is entered: the monsters stand and the draw pile is shuffled, but no monster
        // has its starting powers or an intent yet, and the fight is not in progress (the game's
        // CombatRoom.StartCombat sends "after room entered" between CombatManager.SetUpCombat and
        // AfterCombatRoomLoaded). Relics act here before turn 1: Vajra's Strength, Bronze Scales'
        // Thorns. Only the run's listeners hear it: deck cards, relics, potions.
        var entered = Context;
        var room = RoomKinds.Of(_info.RoomType);
        EachOfRun(l => l.AfterRoomEntered(entered, room));

        // 6. Monster by monster: starting powers, then the first intent (R8).
        foreach (var enemy in _s.Enemies.ToList()) AfterCreatureAdded(enemy);

        // 7. Only now is the fight in progress; Block could not be gained before this (R-damage R8).
        _s.InProgress = true;
        _s.Starting = false;
        Each(l => l.BeforeCombatStart(this), guarded: false);
        Each(l => l.BeforeCombatStartLate(this), guarded: false);

        // 8.
        StartTurn();
    }

    // ---------- the end (R-turns R46 to R48) ----------

    /// <summary>
    /// The safe point where a decided fight actually ends. A pending loss is looked at first, so
    /// a player who dies on the killing blow has lost. Returns true when the fight is over.
    /// </summary>
    internal bool CheckWinCondition()
    {
        if (_s.Over) return true;
        if (_s.PendingLoss)
        {
            _s.PendingLoss = false;
            _s.InProgress = false;
            _s.Over = true;
            _s.Won = false;
            return true;
        }
        if (IsEnding)
        {
            EndCombatWon();
            return true;
        }
        return false;
    }

    private void EndCombatWon()
    {
        _s.InProgress = false;
        _s.Phase = TurnPhase.None;
        Each(l => l.AfterCombatEnd(this), guarded: false);
        _s.History.Clear();

        // The player's powers go without their removal callbacks, the piles are emptied, Block is lost.
        _s.Player.PowerList.Clear();
        foreach (var pile in new[] { _s.Hand, _s.Draw, _s.Discard, _s.Exhaust, _s.Play })
        {
            foreach (var card in pile) card.Pile = PileType.None;
            pile.Clear();
        }
        _s.Player.Block = 0;

        var before = _s.Player.Hp;
        Each(l => l.AfterCombatVictoryEarly(this), guarded: false);
        Each(l => l.AfterCombatVictory(this), guarded: false);
        _s.VictoryHeal = _s.Player.Hp - before;

        _s.Over = true;
        _s.Won = true;
    }

    /// <summary>The arena's one house rule: a fight not won by the end of the last allowed turn is lost.</summary>
    private void TimeOut()
    {
        _s.InProgress = false;
        _s.Over = true;
        _s.Won = false;
        _s.TimedOut = true;
    }
}
