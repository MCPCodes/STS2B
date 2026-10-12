namespace SpireMind.Arena;

/// <summary>
/// One random stream. The generator is xoshiro256** seeded through SplitMix64, as the game's
/// is, and the derived calls scale one 53-bit fraction the way the game's do (rules-turns.md
/// R4), so a decision consumes the same number of draws here as there. Its output depends on
/// the seed alone, on every machine and .NET version.
/// </summary>
public sealed class GameRng
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>Draws made so far; the checks use it to prove which stream a decision consumed.</summary>
    public int Counter { get; private set; }

    public GameRng(ulong seed)
    {
        var z = seed;
        _s0 = SplitMix(ref z);
        _s1 = SplitMix(ref z);
        _s2 = SplitMix(ref z);
        _s3 = SplitMix(ref z);
    }

    private GameRng(GameRng other)
    {
        _s0 = other._s0; _s1 = other._s1; _s2 = other._s2; _s3 = other._s3;
        Counter = other.Counter;
    }

    public GameRng Clone() => new(this);

    private static ulong SplitMix(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextU64()
    {
        unchecked
        {
            Counter++;
            var result = Rotl(_s1 * 5, 7) * 9;
            var t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            return result;
        }
    }

    /// <summary>A fraction in [0, 1): the top 53 bits of one draw.</summary>
    public double NextDouble() => (NextU64() >> 11) * (1.0 / (1UL << 53));

    /// <summary>0 up to but not including <paramref name="maxExclusive"/>. One draw, also when the answer is forced.</summary>
    public int NextInt(int maxExclusive) => (int)(NextDouble() * maxExclusive);

    public int NextInt(int minInclusive, int maxExclusive) => minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));

    /// <summary>A float in [0, max): the fraction times max, narrowed (monster move weights are floats).</summary>
    public float NextFloat(float max = 1f) => (float)(NextDouble() * max);

    public bool NextBool() => NextInt(2) == 0;

    /// <summary>One of <paramref name="items"/>, uniformly. No draw at all when the list is empty.</summary>
    public T? NextItem<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0) return default;
        return items[NextInt(0, items.Count)];
    }

    /// <summary>The game's shuffle: from the last index down to 1, swap with a position at or below it.</summary>
    public void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// The game's other shuffle (its "stable" one): the list is sorted first and shuffled after,
    /// so the result depends on what is in the list and not on the order it came in. The map
    /// generator uses it for the three step directions of a path and for the free map points.
    /// The same draws as <see cref="Shuffle"/>: one per item but the first, none for a list of
    /// one or none. The items must all differ in <paramref name="order"/> (the game's lists do:
    /// three numbers, or map points that each have their own place), since the order two equal
    /// items come out of a sort in is not something to rely on.
    /// </summary>
    /// <param name="order">How to sort; the items' own order when left out.</param>
    public void StableShuffle<T>(List<T> list, Comparison<T>? order = null)
    {
        if (order is null) list.Sort();
        else list.Sort(order);
        Shuffle(list);
    }

    /// <summary>
    /// The game's rounded normal draw (its NextGaussianInt): two numbers make one normal value
    /// (the sine form of the Box-Muller transform, both numbers taken as 1 minus the fraction so
    /// that the logarithm never meets 0), scaled by <paramref name="stdDev"/> around
    /// <paramref name="mean"/> and rounded to the nearest whole number, a half to the even one.
    /// A value outside <paramref name="min"/>..<paramref name="max"/> is thrown away and two new
    /// numbers are drawn, so the call takes 2, 4, 6, ... draws. An act's map draws its number of
    /// Rest Sites and of Unknown rooms this way.
    /// </summary>
    public int NextGaussianInt(int mean, int stdDev, int min, int max)
    {
        int value;
        do
        {
            var u = 1.0 - NextDouble();
            var v = 1.0 - NextDouble();
            var normal = Math.Sqrt(-2.0 * Math.Log(u)) * Math.Sin(Math.PI * 2.0 * v);
            value = (int)Math.Round(mean + stdDev * normal);
        }
        while (value < min || value > max);
        return value;
    }
}

/// <summary>
/// The streams of one fight, kept apart as the game keeps them (rules-turns.md R4), so that a
/// shuffle never shifts a monster's move and a random target never shifts a shuffle.
/// </summary>
public sealed class RngSet
{
    /// <summary>First shuffle, reshuffles, "random position" inserts, Stampede's pick.</summary>
    public GameRng Shuffle { get; private set; }

    /// <summary>Random move choices of every monster (one shared stream).</summary>
    public GameRng MonsterAi { get; private set; }

    /// <summary>Monster HP rolls.</summary>
    public GameRng Niche { get; private set; }

    /// <summary>Random targets: attacks at a random enemy, auto-played cards, Juggernaut.</summary>
    public GameRng CombatTargets { get; private set; }

    /// <summary>Random card picks from the player's own piles (Cinder, True Grit, Thrash, Aggression).</summary>
    public GameRng CombatCardSelection { get; private set; }

    /// <summary>Random new cards (Infernal Blade, Stoke, the card potions).</summary>
    public GameRng CombatCardGeneration { get; private set; }

    public GameRng CombatPotionGeneration { get; private set; }

    public GameRng CombatEnergyCosts { get; private set; }

    /// <summary>The encounter's own stream: it only chooses the line-up.</summary>
    public GameRng Encounter { get; private set; }

    public RngSet(int seed)
    {
        Shuffle = For(seed, "shuffle");
        MonsterAi = For(seed, "monster_ai");
        Niche = For(seed, "niche");
        CombatTargets = For(seed, "combat_targets");
        CombatCardSelection = For(seed, "combat_card_selection");
        CombatCardGeneration = For(seed, "combat_card_generation");
        CombatPotionGeneration = For(seed, "combat_potion_generation");
        CombatEnergyCosts = For(seed, "combat_energy_costs");
        Encounter = For(seed, "encounter");
    }

    /// <summary>
    /// The streams of a fight of a run, made by the run (RunStreams.LendToFight): its own long
    /// streams as they stand, so this fight goes on where the last one stopped, and the
    /// encounter's own stream as the game seeds it. The set owns the streams it is given.
    /// </summary>
    public RngSet(GameRng shuffle, GameRng monsterAi, GameRng niche, GameRng combatTargets, GameRng combatCardSelection,
        GameRng combatCardGeneration, GameRng combatPotionGeneration, GameRng combatEnergyCosts, GameRng encounter)
    {
        Shuffle = shuffle;
        MonsterAi = monsterAi;
        Niche = niche;
        CombatTargets = combatTargets;
        CombatCardSelection = combatCardSelection;
        CombatCardGeneration = combatCardGeneration;
        CombatPotionGeneration = combatPotionGeneration;
        CombatEnergyCosts = combatEnergyCosts;
        Encounter = encounter;
    }

    private RngSet(RngSet o)
    {
        Shuffle = o.Shuffle.Clone();
        MonsterAi = o.MonsterAi.Clone();
        Niche = o.Niche.Clone();
        CombatTargets = o.CombatTargets.Clone();
        CombatCardSelection = o.CombatCardSelection.Clone();
        CombatCardGeneration = o.CombatCardGeneration.Clone();
        CombatPotionGeneration = o.CombatPotionGeneration.Clone();
        CombatEnergyCosts = o.CombatEnergyCosts.Clone();
        Encounter = o.Encounter.Clone();
    }

    public RngSet Clone() => new(this);

    /// <summary>A named stream of a seed: the seed plus a hash of the name, as the game derives its run streams.</summary>
    public static GameRng For(int seed, string name) => new(unchecked((uint)seed + (uint)NameHash(name)));

    /// <summary>
    /// A stable 32-bit hash of a stream name: two accumulators over the even and the odd
    /// characters, combined at the end. Any stable hash would do; this is the shape the
    /// encounter spec describes for the game's own.
    /// </summary>
    public static int NameHash(string name)
    {
        unchecked
        {
            int h1 = 352654597, h2 = 352654597;
            for (var i = 0; i < name.Length; i += 2)
            {
                h1 = ((h1 << 5) + h1) ^ name[i];
                if (i + 1 < name.Length) h2 = ((h2 << 5) + h2) ^ name[i + 1];
            }
            return h1 + h2 * 1566083941;
        }
    }
}
