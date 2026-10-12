using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// Makes the potions the game makes at random outside a fight: a fight's potion drop, a shop's
/// three potions, the potions of events. A port of the game's PotionFactory.
/// <para>
/// One potion takes two numbers: the rarity (Rare at or below 0.1, Uncommon at or below 0.35,
/// else Common), then one of the potions of that rarity the character can meet, each as likely
/// as the others. The list is the game's: the character's own three potions, then the 45
/// shared ones, each group in the game's order, without those of a timeline epoch the profile
/// has not revealed.
/// </para>
/// <para>
/// The roll runs over the game's full list, potions the arena has not built included. One that
/// lands on such a potion is rolled again, rarity and pick, from the same stream, as the
/// single-fight scenarios have always done it: a house rule, written to the run's
/// <see cref="LeftOutLog"/>; in strict mode the run cannot go on instead.
/// </para>
/// </summary>
public static class PotionFactory
{
    /// <summary>A potion is Rare at or below this roll.</summary>
    public const float RareThreshold = 0.1f;

    /// <summary>... Uncommon at or below this one, Common above it.</summary>
    public const float UncommonThreshold = 0.35f;

    /// <summary>How many of the list's first potions are the Ironclad's own; they need his fourth epoch.</summary>
    private const int CharacterPotions = 3;
    private const string CharacterPotionEpoch = "IRONCLAD4_EPOCH";

    /// <summary>The shared potions that wait behind a timeline epoch.</summary>
    private static readonly IReadOnlyDictionary<string, string> PotionEpochs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BeetleJuice"] = "POTION1_EPOCH", ["MazalethsGift"] = "POTION1_EPOCH", ["DropletOfPrecognition"] = "POTION1_EPOCH",
        ["PowderedDemise"] = "POTION2_EPOCH", ["ShipInABottle"] = "POTION2_EPOCH", ["TouchOfInsanity"] = "POTION2_EPOCH",
    };

    /// <summary>
    /// Every potion a random potion of this run can be, in the game's order, built or not
    /// (PotionFactory.GetPotionOptions): the character's pool, then the shared pool, each without
    /// what the profile has not unlocked, and without the names on the blacklist.
    /// </summary>
    public static List<(string ClassName, PotionRarity Rarity)> GetPotionOptions(Run run, IEnumerable<string>? blacklist = null)
    {
        var unlocks = run.State.FindPart<RunWorld>() is { IsBegun: true } world ? world.Unlocks : RunUnlocks.All;
        var banned = blacklist is null ? null : new HashSet<string>(blacklist, StringComparer.Ordinal);
        var options = new List<(string, PotionRarity)>();
        for (var i = 0; i < Scenarios.PotionPool.Count; i++)
        {
            var potion = Scenarios.PotionPool[i];
            var epoch = i < CharacterPotions ? CharacterPotionEpoch : PotionEpochs.GetValueOrDefault(potion.ClassName);
            if (!unlocks.IsRevealed(epoch)) continue;
            if (banned is not null && banned.Contains(potion.ClassName)) continue;
            options.Add(potion);
        }
        return options;
    }

    /// <summary>One random potion outside a fight (PotionFactory.CreateRandomPotionOutOfCombat): any potion of the list, the healing ones included.</summary>
    /// <param name="rng">The stream of this one decision: the caller's draw of the stream the game uses here.</param>
    /// <exception cref="RunUnrepresentableException">No potion of the list is built, or strict mode met one that is not.</exception>
    public static PotionDef CreateRandomPotionOutOfCombat(Run run, GameRng rng, IEnumerable<string>? blacklist = null) =>
        CreateRandomPotions(run, GetPotionOptions(run, blacklist), 1, rng)[0];

    /// <summary>Several random potions outside a fight, no two the same (a shop's shelf).</summary>
    /// <exception cref="RunUnrepresentableException">No potion of the list is built, or strict mode met one that is not.</exception>
    public static List<PotionDef> CreateRandomPotionsOutOfCombat(Run run, int count, GameRng rng, IEnumerable<string>? blacklist = null) =>
        CreateRandomPotions(run, GetPotionOptions(run, blacklist), count, rng);

    /// <summary>The rarity a roll gives. The game compares with "at or below" here, unlike the card and relic rolls.</summary>
    public static PotionRarity RarityOf(float roll) =>
        roll <= RareThreshold ? PotionRarity.Rare : roll <= UncommonThreshold ? PotionRarity.Uncommon : PotionRarity.Common;

    private static List<PotionDef> CreateRandomPotions(Run run, List<(string ClassName, PotionRarity Rarity)> options, int count, GameRng rng)
    {
        var registry = Content.Registry;
        var made = new List<PotionDef>(count);
        for (var i = 0; i < count; i++)
        {
            if (!options.Any(option => registry.Has(ContentKind.Potion, option.ClassName)))
                throw new RunUnrepresentableException(string.Create(CultureInfo.InvariantCulture,
                    $"a random potion on floor {run.State.TotalFloor} can only land on potions the arena leaves out"));
            while (true)
            {
                var rarity = RarityOf(rng.NextFloat());
                var ofRarity = options.Where(option => option.Rarity == rarity).ToList();
                // The game picks from an empty list without a number and then fails on the missing potion.
                if (ofRarity.Count == 0)
                    throw new InvalidOperationException($"No {rarity} potion is left to pick: the game cannot make this potion either.");
                var landed = ofRarity[rng.NextInt(0, ofRarity.Count)];
                if (registry.Has(ContentKind.Potion, landed.ClassName))
                {
                    made.Add(registry.Potion(landed.ClassName));
                    options.Remove(landed);
                    break;
                }
                run.State.Part<LeftOutLog>().PassedOver(run.State, "potion", landed.ClassName, "a random potion");
            }
        }
        return made;
    }
}
