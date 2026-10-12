using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// One relic taken out of a bag, or the reason none could be.
/// </summary>
/// <param name="Relic">The class name of the relic pulled; null when none could be (<see cref="Reason"/> says why).</param>
/// <param name="Asked">The rarity first asked for (the first one rolled, when the pull rolled it).</param>
/// <param name="GameRelic">The relic the game itself would have handed out at this point, built or not; null when its bags hold nothing more.</param>
/// <param name="PassedOver">Relics the game would have reached before <see cref="Relic"/> and the arena has not built, in pull order. They stay in the bag.</param>
/// <param name="Rerolls">How often the rarity was rolled again because no built relic of the rolled rarity was left.</param>
/// <param name="Reason">Why nothing was pulled, in one line, for <see cref="RunResult.Unrepresentable"/>; null when a relic was.</param>
public sealed record RelicPull(string? Relic, RelicRarity Asked, string? GameRelic, IReadOnlyList<string> PassedOver, int Rerolls, string? Reason = null)
{
    public bool Found => Relic is not null;

    /// <summary>True when the relic is exactly the one the game gives: nothing was passed over and no rarity rolled again.</summary>
    public bool IsTheGames => Found && PassedOver.Count == 0 && Rerolls == 0;

    /// <summary>The relic's rarity; None when nothing was pulled.</summary>
    public RelicRarity Rarity => Relic is null ? RelicRarity.None : GameLists.BagRelic(Relic).Rarity;
}

/// <summary>
/// Pulling relics from the run's two bags as the game's RelicFactory does, and what the game's
/// relic classes say about themselves that a pull has to know also of relics the arena has not
/// built (which may not come up in act 3, which a shop never sells).
/// <code>
/// var rng = world.Streams.Draw(RunStream.Rewards, Purpose.RelicRarity(row));
/// var pull = RunRelics.RollAndPullFromFront(run.State, rng);          // an elite's relic, an event's, a capsule's
/// if (!pull.Found) { run.End(RunResult.Unrepresentable, pull.Reason); return; }
/// run.ObtainRelic(pull.Relic!);                                        // when it is taken
/// </code>
/// <list type="bullet">
/// <item>The rarity: one number, Common below 0.5, Uncommon below 0.83, else Rare (<see cref="RollRarity"/>).</item>
/// <item>Elites, events and Neow's capsules pull from the FRONT of the player's bag; shops from its
/// BACK, never a relic the game keeps out of shops; a chest from the front of the SHARED bag.</item>
/// <item>A rarity whose list is empty gives the next one's: Shop, Common, Uncommon, Rare, then nothing
/// (<see cref="RelicBag"/>). A relic the run no longer allows is dropped from the bag at any pull.</item>
/// <item>A relic pulled from the player's bag leaves the shared bag at once; one pulled from the shared
/// bag leaves the player's when it is obtained (<see cref="Run.ObtainRelic(Relic, int)"/> takes every
/// obtained relic out of both).</item>
/// </list>
/// <para>
/// <b>Relics the arena has not built</b> (a house rule; the plan's "draw again"). A pull lands
/// on what the game would give. If that relic is not built: in strict mode nothing is pulled
/// and the caller ends the run as unrepresentable; otherwise it is passed over and the next
/// built relic of the same list is taken, in pull order. Passed-over relics stay in the bag
/// (they would be passed over again, so nothing can ever show them). When the list holds no
/// built relic at all, a rolled rarity is rolled again on the same stream; a rarity that was
/// not rolled (a shop's third slot) gives nothing, and the run is unrepresentable. The game's
/// own fall-through to the next rarity happens only when a list is really empty.
/// </para>
/// </summary>
public static class RunRelics
{
    // ---------- what the game's relic classes say about themselves ----------

    /// <summary>The relics a shop never sells (IsAllowedInShops false in their classes).</summary>
    public static readonly IReadOnlySet<string> NeverInShops = new HashSet<string>(StringComparer.Ordinal)
    {
        "AmethystAubergine", "BowlerHat", "LuckyFysh", "OldCoin", "TheCourier",
    };

    /// <summary>
    /// The bag relics that may only come up before the chest of act 3 (their IsAllowed is
    /// RelicModel.IsBeforeAct3TreasureChest): relics that pay off over many rooms.
    /// </summary>
    public static readonly IReadOnlySet<string> OnlyBeforeTheLastChest = new HashSet<string>(StringComparer.Ordinal)
    {
        "AmethystAubergine", "BookOfFiveRings", "BowlerHat", "DragonFruit", "FrozenEgg", "Girya", "JuzuBracelet", "LastingCandy", "LuckyFysh",
        "MealTicket", "MoltenEgg", "OldCoin", "Planisphere", "Shovel", "ToxicEgg", "WhiteBeastStatue", "WhiteStar",
    };

    /// <summary>The total floor from which those relics no longer come up in single player (the game's test is "total floor below 41").</summary>
    public const int LastChestFloor = 41;

    /// <summary>
    /// Whether a bag relic may come up now (RelicModel.IsAllowed, single player). A relic that
    /// may not is dropped from the bag for good at the next pull. In act 1 every bag relic is
    /// allowed, except Lasting Candy for a profile that has never finished a run.
    /// </summary>
    public static bool IsAllowed(string relic, RunState state, RunUnlocks unlocks)
    {
        if (relic == "LastingCandy" && unlocks.NumberOfRuns == 0) return false;
        return !OnlyBeforeTheLastChest.Contains(relic) || state.TotalFloor < LastChestFloor;
    }

    /// <summary>True when the arena holds the relic: it is registered, so it can be made and shown.</summary>
    public static bool IsBuilt(string relic) => Content.Registry.Has(ContentKind.Relic, relic);

    /// <summary>Why the arena does not hold a relic: the reason it was left out with, or that it is not built yet.</summary>
    public static string WhyNotBuilt(string relic) =>
        Content.Registry.IsExcluded(ContentKind.Relic, relic, out var reason) ? reason : "not built yet";

    // ---------- the rarity roll ----------

    /// <summary>RelicFactory.RollRarity: one number; Common below 0.5, Uncommon below 0.83, else Rare. Never Shop.</summary>
    public static RelicRarity RollRarity(GameRng rng)
    {
        var roll = rng.NextFloat();
        if (roll < 0.5f) return RelicRarity.Common;
        return roll < 0.83f ? RelicRarity.Uncommon : RelicRarity.Rare;
    }

    // ---------- the pulls ----------

    /// <summary>
    /// The next relic of a rarity from the front of the player's bag (RelicFactory.PullNextRelicFromFront):
    /// an elite's relic, a random-relic event, Neow's capsules. It leaves the shared bag too.
    /// </summary>
    /// <param name="rerollWith">The stream the rarity was rolled on, when it was rolled: the rarity is rolled
    /// again from it when no built relic of the rolled rarity is left. Null for a rarity that was not rolled.</param>
    /// <param name="strict">True pulls nothing when the game's relic is not built (the caller ends the run as unrepresentable).</param>
    /// <param name="filter">The caller's own condition on the relic, as the game's callers pass one.</param>
    /// <param name="isBuilt">Which relics the arena holds; the registry when left out. For checks.</param>
    public static RelicPull PullFromFront(RunState state, RelicRarity rarity, GameRng? rerollWith = null, bool strict = false,
        Func<string, bool>? filter = null, Func<string, bool>? isBuilt = null) =>
        Pull(state, End.PlayerFront, rarity, rerollWith, strict, filter, isBuilt);

    /// <summary>The same with the roll: one number for the rarity from <paramref name="rng"/>, then the pull.</summary>
    public static RelicPull RollAndPullFromFront(RunState state, GameRng rng, bool strict = false,
        Func<string, bool>? filter = null, Func<string, bool>? isBuilt = null) =>
        Pull(state, End.PlayerFront, RollRarity(rng), rng, strict, filter, isBuilt);

    /// <summary>
    /// The next relic of a rarity from the back of the player's bag that a shop may sell
    /// (RelicFactory.PullNextRelicFromBack with the shop's filter). It has left both bags whether
    /// or not it is bought.
    /// </summary>
    public static RelicPull PullFromBack(RunState state, RelicRarity rarity, GameRng? rerollWith = null, bool strict = false,
        Func<string, bool>? filter = null, Func<string, bool>? isBuilt = null) =>
        Pull(state, End.PlayerBack, rarity, rerollWith, strict, filter, isBuilt);

    /// <summary>
    /// The next relic of a rarity from the front of the shared bag: a chest's relic
    /// (TreasureRoomRelicSynchronizer.BeginRelicPicking). It leaves the player's bag when it is obtained.
    /// </summary>
    public static RelicPull PullFromShared(RunState state, RelicRarity rarity, GameRng? rerollWith = null, bool strict = false,
        Func<string, bool>? isBuilt = null) =>
        Pull(state, End.SharedFront, rarity, rerollWith, strict, null, isBuilt);

    /// <summary>Takes a relic out of both bags of the run, wherever it is. Nothing happens in a run without a world.</summary>
    public static void TakeOutOfBags(RunState state, string relic)
    {
        if (state.FindPart<RunWorld>() is not { IsBegun: true } world) return;
        world.PlayerBag.Remove(relic);
        world.SharedBag.Remove(relic);
    }

    /// <summary>
    /// Takes every relic the player holds out of both bags. A relic obtained outside a fight has
    /// left them already; one obtained inside a fight (which cannot reach the bags) leaves them
    /// here, before the next pull looks at a bag. Every pull calls it.
    /// </summary>
    public static void SyncBags(RunState state)
    {
        foreach (var relic in state.Relics) TakeOutOfBags(state, relic.ClassName);
    }

    private enum End { PlayerFront, PlayerBack, SharedFront }

    private static readonly RelicRarity[] Rolled = [RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare];

    private static RelicPull Pull(RunState state, End end, RelicRarity rarity, GameRng? rng, bool strict, Func<string, bool>? filter, Func<string, bool>? isBuilt)
    {
        if (state.FindPart<RunWorld>() is not { IsBegun: true } world)
            throw new InvalidOperationException("The run's world was not begun: it has no relic bags to pull from (RunWorld.Begin).");
        SyncBags(state);

        var bag = end == End.SharedFront ? world.SharedBag : world.PlayerBag;
        var built = isBuilt ?? IsBuilt;
        bool Allowed(string relic) => IsAllowed(relic, state, world.Unlocks);
        bool GameFits(string relic) => (end != End.PlayerBack || !NeverInShops.Contains(relic)) && (filter is null || filter(relic));
        bool Fits(string relic) => GameFits(relic) && built(relic);
        string? Take(RelicBag from, RelicRarity r, Func<string, bool> fits) =>
            end == End.PlayerBack ? from.PullFromBack(r, Allowed, fits) : from.PullFromFront(r, Allowed, fits);

        var asked = rarity;
        string? gameRelic = null;
        var passedOver = new List<string>();
        var rerolls = 0;
        RelicPull Nothing(string reason) => new(null, asked, gameRelic, passedOver, rerolls, reason);

        while (true)
        {
            // What the game would pull, looked up on a copy so that nothing is taken yet.
            var probe = bag.Clone();
            var would = Take(probe, rarity, GameFits);
            if (rerolls == 0) gameRelic = would;
            if (would is null)
                return Nothing("the relic bag holds no relic the game could give here; the game then hands out its fallback relic, Circlet, which the arena does not hold");

            if (built(would))
            {
                var taken = Take(bag, rarity, GameFits)!;
                if (end != End.SharedFront) world.SharedBag.Remove(taken);
                return new RelicPull(taken, asked, gameRelic, passedOver, rerolls);
            }
            if (strict)
                return Nothing(string.Create(CultureInfo.InvariantCulture, $"the game gives the relic {would} here, which the arena does not hold ({WhyNotBuilt(would)})"));

            // The list the game's pull landed on (after its own fall-through). The next built
            // relic of that list is taken; the ones before it are passed over and stay.
            var landed = GameLists.BagRelic(would).Rarity;
            var next = Take(bag.Clone(), landed, Fits);
            if (next is not null && GameLists.BagRelic(next).Rarity == landed)
            {
                passedOver.Add(would);
                var rest = probe.Relics(landed);
                foreach (var relic in end == End.PlayerBack ? rest.Reverse() : rest)
                {
                    if (relic == next) break;
                    if (GameFits(relic)) passedOver.Add(relic);
                }
                var taken = Take(bag, landed, Fits)!;
                if (end != End.SharedFront) world.SharedBag.Remove(taken);
                return new RelicPull(taken, asked, gameRelic, passedOver, rerolls);
            }

            // No built relic of that rarity is left. A rolled rarity is rolled again, while some
            // rolled rarity still has a built relic to land on.
            if (rng is null || !Rolled.Contains(rarity))
                return Nothing(string.Create(CultureInfo.InvariantCulture, $"no {landed} relic the arena holds is left in the bag, and the rarity was not rolled (the game gives {would})"));
            var anyLeft = Rolled.Any(r => Take(bag.Clone(), r, Fits) is { } some && GameLists.BagRelic(some).Rarity == r);
            if (!anyLeft || rerolls >= 1000)
                return Nothing(string.Create(CultureInfo.InvariantCulture, $"no relic the arena holds is left in the bag in any rarity a roll can give (the game gives {would})"));
            rarity = RollRarity(rng);
            rerolls++;
        }
    }
}
