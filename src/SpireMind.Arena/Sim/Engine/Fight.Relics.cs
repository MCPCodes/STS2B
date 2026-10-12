namespace SpireMind.Arena;

// Gold and relics gained or lost inside a fight, and the player outside a fight.
//
// In the game the player is one creature for the whole run, and the commands that heal it,
// hurt it, change its max HP or kill it are the same in a fight and outside one; outside,
// the hooks they send reach the run's listeners alone (deck cards, relics, potions). Here a
// fight works on its own player, so "outside a fight" needs a place for that player to stand
// while such a command runs: a Fight that is no combat (OutOfCombat). It has the player, the
// relics, the belt and the deck, no enemy and no pile, and it is never in progress, so the
// commands of Fight.Damage do there what the game's do with no combat state: Tungsten Rod
// takes 1 off an event's HP loss, Lizard Tail and Fairy in a Bottle prevent a death in an
// event room, and nothing that belongs to a running fight answers.
public sealed partial class Fight
{
    /// <summary>The <see cref="EncounterName"/> of the player outside a fight (<see cref="IsCombat"/> false).</summary>
    public const string OutOfCombatName = "(no fight)";

    private bool _outOfCombat;

    /// <summary>
    /// True for a fight. False for the player outside one, where a hook of the fight is heard
    /// because a command of the run hurt or healed the player (the game's "the current room is
    /// not a combat room"): there are no enemies, no piles and no turns, and
    /// <see cref="InProgress"/> is false. A relic whose game code tests for a running fight or
    /// for a combat room tests <see cref="InProgress"/> or this.
    /// </summary>
    public bool IsCombat => !_outOfCombat;

    /// <summary>What a hook of the run is handed inside this fight; <see cref="RunHooks"/> takes it too
    /// (a potion gained in a fight: <c>RunHooks.AfterPotionProcured(f.Context, potion)</c>).</summary>
    public RunContext Context => new(this);

    private Fight(FightInfo info, FightState state)
    {
        _info = info;
        _s = state;
        _outOfCombat = true;
    }

    /// <summary>
    /// The run's player outside a fight, for one command of the run (<see cref="Run.OutOfCombat"/>).
    /// The relic objects and the deck cards are the run's own, not copies: a relic that changes
    /// one of its fields here has changed it for the run. HP, max HP, gold and the belt are
    /// copies; the run writes them back when the command is done (<see cref="HandBackShared"/>).
    /// The streams are a set of their own that nothing may draw from (<see cref="DrewRandomNumbers"/>).
    /// </summary>
    internal static Fight OutOfCombat(RunHandOver player, int hp, int maxHp, int floor, int maxTurns)
    {
        var state = new FightState { Rng = new RngSet(0) };
        // Not "in combat": a heal is then silent, as the game's is for a creature with no combat state.
        state.Player = new Creature(state.NextCreatureId++, Side.Player, PlayerName, Math.Min(hp, maxHp), maxHp, null, null) { InCombat = false };
        state.Relics.AddRange(player.Relics);
        state.Deck.AddRange(player.Deck);
        state.Potions = player.Belt.ToArray();
        state.Gold = Math.Max(0, player.Gold);
        state.NextDeckCardId = player.NextDeckCardId;
        return new Fight(new FightInfo([], [], RoomType.Monster, OutOfCombatName, floor, 0, maxTurns), state);
    }

    /// <summary>What <see cref="OutOfCombat"/> hands back: the same relic and card objects in the lists as
    /// they stand, the belt, the gold. HP and max HP are read off <see cref="Player"/>.</summary>
    internal RunHandOver HandBackShared() => new(_s.Deck.ToList(), _s.Relics.ToList(), _s.Potions.ToArray(), _s.Gold, _s.NextDeckCardId);

    /// <summary>True when anything drew a number from this fight's streams.</summary>
    internal bool DrewRandomNumbers =>
        _s.Rng.Shuffle.Counter + _s.Rng.MonsterAi.Counter + _s.Rng.Niche.Counter + _s.Rng.CombatTargets.Counter + _s.Rng.CombatCardSelection.Counter
        + _s.Rng.CombatCardGeneration.Counter + _s.Rng.CombatPotionGeneration.Counter + _s.Rng.CombatEnergyCosts.Counter + _s.Rng.Encounter.Counter > 0;

    // ---------- gold (PlayerCmd.GainGold, LoseGold) ----------

    /// <summary>
    /// Gives the player gold inside a fight, through the gold hooks: every listener of the fight
    /// may change the amount (Bowler Hat), those that did are told, the whole-number part is
    /// paid, and the run's listeners hear "gold gained" (Dragon Fruit). Nothing is paid, and
    /// nobody hears "gold gained", when the amount ends at 0 or less. Returns what was paid.
    /// </summary>
    public int GainGold(decimal amount) => RunHooks.GainGold(Context, amount);

    /// <summary>Takes gold, never below 0. No hook hears it.</summary>
    public void LoseGold(decimal amount) => Gold = Math.Max(0, Gold - (int)amount);

    // ---------- relics (RelicCmd.Obtain, Remove, Replace) ----------

    /// <summary>
    /// Gives the player a relic inside a fight: it joins the relics at the end (or at
    /// <paramref name="index"/>) and its pickup effect runs. It hears hooks from the next
    /// dispatch on. The relic bags are the run's: the run takes the relic out of them when it
    /// next looks at a bag (<see cref="RunRelics.SyncBags"/>), and no pull can come before that.
    /// </summary>
    public Relic ObtainRelic(Relic relic, int index = -1)
    {
        if (_s.Relics.Contains(relic)) throw new InvalidOperationException($"The player already holds this {relic.ClassName}.");
        if (index < 0) _s.Relics.Add(relic);
        else _s.Relics.Insert(index, relic);
        relic.AfterObtained(Context);
        return relic;
    }

    /// <summary>Takes a relic away from the player. It hears no hook from here on, not even the rest of a running dispatch.</summary>
    public void RemoveRelic(Relic relic)
    {
        if (!_s.Relics.Remove(relic)) throw new InvalidOperationException($"The player does not hold this {relic.ClassName}.");
        _relicsLeft = true;
        relic.AfterRemoved(Context);
    }

    /// <summary>Takes <paramref name="original"/> away and gives <paramref name="replacement"/> in its place in the relic list.</summary>
    public Relic ReplaceRelic(Relic original, Relic replacement)
    {
        var index = _s.Relics.IndexOf(original);
        RemoveRelic(original);
        return ObtainRelic(replacement, index);
    }
}
