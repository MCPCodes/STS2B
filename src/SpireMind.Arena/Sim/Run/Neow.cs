using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// The relics Neow can offer, by the game's class names and in the order of the game's own
/// lists (Neow.cs: the order matters, since the picks are indexes into these lists).
/// </summary>
public static class NeowRelics
{
    /// <summary>The eight relics that come with a price. One of them is the third option.</summary>
    public static readonly IReadOnlyList<string> Cursed =
    [
        "CursedPearl", "HeftyTablet", "LargeCapsule", "LeafyPoultice", "NeowsBones", "PrecariousShears", "SilkenTress", "SilverCrucible",
    ];

    /// <summary>The fourteen relics the positive list always starts with.</summary>
    public static readonly IReadOnlyList<string> Positive =
    [
        "ArcaneScroll", "BoomingConch", "FishingRod", "GoldenPearl", "Kaleidoscope", "LeadPaperweight", "LostCoffer", "MassiveScroll",
        "NeowsTorment", "NewLeaf", "PhialHolster", "PreciseScissors", "ScrollBoxes", "WingedBoots",
    ];

    /// <summary>Three coins, each adding one relic of a pair to the positive list: heads the first, tails the second. In this order.</summary>
    public static readonly IReadOnlyList<(string Heads, string Tails)> Coins =
    [
        ("LavaRock", "SmallCapsule"), ("NutritiousOyster", "StoneHumidifier"), ("NeowsTalisman", "Pomander"),
    ];

    /// <summary>A cursed relic takes its plain twin out of the positive list.</summary>
    public static readonly IReadOnlyDictionary<string, string> Twins = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CursedPearl"] = "GoldenPearl", ["HeftyTablet"] = "ArcaneScroll", ["LeafyPoultice"] = "NewLeaf", ["PrecariousShears"] = "PreciseScissors",
    };

    /// <summary>With this cursed relic the first coin is not flipped at all, and neither relic of its pair is in the list.</summary>
    public const string NoFirstCoin = "LargeCapsule";

    /// <summary>All twenty-eight, the positive ones first.</summary>
    public static readonly IReadOnlyList<string> All =
        Positive.Concat(Coins.SelectMany(pair => new[] { pair.Heads, pair.Tails })).Concat(Cursed).ToList();

    /// <summary>
    /// Whether Neow may offer a relic to this player (the relic's IsAllowedAtNeow). Massive Scroll
    /// needs a second player; Winged Boots and Silver Crucible need a run of one; Kaleidoscope a
    /// profile that has unlocked all five characters; Scroll Boxes a character pool with at least
    /// four Commons and two Uncommons unlocked. Everything else is always allowed.
    /// </summary>
    /// <param name="players">The number of players of the run. The arena plays runs of one; a check reads real co-op records.</param>
    public static bool IsAllowed(string relic, RunUnlocks unlocks, int players = 1) => relic switch
    {
        "MassiveScroll" => players > 1,
        "WingedBoots" or "SilverCrucible" => players == 1,
        "Kaleidoscope" => OtherCharacters.All(unlocks.IsRevealed),
        "ScrollBoxes" => CanGenerateBundles(unlocks),
        _ => true,
    };

    /// <summary>The epochs that unlock the other four characters; the Ironclad needs none.</summary>
    private static readonly string[] OtherCharacters = ["SILENT1_EPOCH", "REGENT1_EPOCH", "NECROBINDER1_EPOCH", "DEFECT1_EPOCH"];

    private static bool CanGenerateBundles(RunUnlocks unlocks)
    {
        var cards = CardPools.Character.Unlocked(unlocks);
        return cards.Count(card => card.Rarity == CardRarity.Common) >= 4 && cards.Count(card => card.Rarity == CardRarity.Uncommon) >= 2;
    }
}

/// <summary>
/// Neow's three options for one run: two positive relics and a cursed one, in the order of the
/// screen. The game makes them on Neow's own random stream, so a seed fixes them
/// (Neow.GenerateInitialOptions):
/// <list type="number">
/// <item>the cursed relic: one pick among the cursed relics this player may be offered;</item>
/// <item>the positive list: the fourteen, without the cursed relic's twin;</item>
/// <item>a coin for Lava Rock or Small Capsule, unless the cursed relic is Large Capsule;</item>
/// <item>a coin for Nutritious Oyster or Stone Humidifier, a coin for Neow's Talisman or Pomander;</item>
/// <item>what this player may not be offered is taken out;</item>
/// <item>the list is shuffled as it stands and its first two are offered, then the cursed one.</item>
/// </list>
/// <para>
/// <b>Relics the arena has not built</b> (a house rule; the plan's "draw again"). A cursed pick
/// that lands on a relic that is not built is drawn again from the same stream; of the shuffled
/// positive list the first two built relics are offered and the ones before them are passed
/// over. Either changes which run this is, so everything passed over is listed
/// (<see cref="PassedOver"/>); in strict mode nothing is passed over and the offer has none
/// (<see cref="Reason"/>). <see cref="GameRelics"/> is always the game's own offer for the seed.
/// </para>
/// </summary>
/// <param name="Relics">The offer as the arena shows it: two positive relics, then the cursed one. Empty when it can show none.</param>
/// <param name="GameRelics">The offer the game makes on this stream, whatever is built.</param>
/// <param name="PassedOver">What the rolls landed on and passed over because it is not built, in the order met.</param>
/// <param name="Reason">Why there is no offer, in one line; null when there is one.</param>
public sealed record NeowOffer(IReadOnlyList<string> Relics, IReadOnlyList<string> GameRelics, IReadOnlyList<string> PassedOver, string? Reason = null)
{
    public bool Found => Reason is null;

    /// <summary>The offer is the game's own for the seed.</summary>
    public bool IsTheGames => Found && PassedOver.Count == 0;

    /// <summary>The offer the game makes for a seed and a profile: what a player who types the seed into the game meets.</summary>
    public static IReadOnlyList<string> OfTheGame(RunStreams streams, RunUnlocks unlocks) => Game(streams.Event(nameof(Neow)), unlocks, 1);

    /// <summary>
    /// Makes the offer from Neow's stream as it stands.
    /// </summary>
    /// <param name="rng">Neow's own stream (<c>world.Streams.Event("Neow")</c>); every number of the offer comes from it.</param>
    /// <param name="isBuilt">Which relics the arena holds; the registry when left out. For checks.</param>
    /// <param name="strict">Nothing is drawn again or passed over: an offer with a relic that is not built is no offer.</param>
    /// <param name="players">The number of players of the run; see <see cref="NeowRelics.IsAllowed"/>.</param>
    public static NeowOffer Roll(GameRng rng, RunUnlocks unlocks, Func<string, bool>? isBuilt = null, bool strict = false, int players = 1)
    {
        isBuilt ??= RunRelics.IsBuilt;
        var game = Game(rng.Clone(), unlocks, players);
        var passedOver = new List<string>();
        NeowOffer None(string reason) => new([], game, passedOver, reason);

        if (strict)
        {
            if (game.FirstOrDefault(relic => !isBuilt(relic)) is { } missing)
                return None($"Neow offers {string.Join(", ", game)} on this seed, and the arena leaves out {missing} ({RunRelics.WhyNotBuilt(missing)})");
            Game(rng, unlocks, players);
            return new NeowOffer(game, game, passedOver);
        }

        // The cursed relic: the game's pick, drawn again while it lands on a relic that is not built.
        var cursedChoices = NeowRelics.Cursed.Where(relic => NeowRelics.IsAllowed(relic, unlocks, players)).ToList();
        if (!cursedChoices.Any(isBuilt))
            return None($"Neow's cursed relic can only be one the arena leaves out ({string.Join(", ", cursedChoices)})");
        string cursed;
        while (true)
        {
            cursed = cursedChoices[rng.NextInt(0, cursedChoices.Count)];
            if (isBuilt(cursed)) break;
            passedOver.Add(cursed);
        }

        // The positive relics: the shuffled list in its order, the relics that are not built passed over.
        var shuffled = ShuffledPositives(rng, cursed, unlocks, players);
        var shown = new List<string>(3);
        foreach (var relic in shuffled)
        {
            if (shown.Count == 2) break;
            if (isBuilt(relic)) shown.Add(relic);
            else passedOver.Add(relic);
        }
        if (shown.Count < 2)
            return None($"Neow's positive relics hold fewer than two the arena has built (in the order of this seed: {string.Join(", ", shuffled)})");
        shown.Add(cursed);
        return new NeowOffer(shown, game, passedOver);
    }

    /// <summary>The game's own offer: the steps of the class comment, with nothing drawn again.</summary>
    private static List<string> Game(GameRng rng, RunUnlocks unlocks, int players)
    {
        var cursedChoices = NeowRelics.Cursed.Where(relic => NeowRelics.IsAllowed(relic, unlocks, players)).ToList();
        var cursed = cursedChoices[rng.NextInt(0, cursedChoices.Count)];
        var shuffled = ShuffledPositives(rng, cursed, unlocks, players);
        return [shuffled[0], shuffled[1], cursed];
    }

    /// <summary>Steps 2 to 6: the positive list for a cursed relic, its coins, what the player may not be offered taken out, shuffled.</summary>
    private static List<string> ShuffledPositives(GameRng rng, string cursed, RunUnlocks unlocks, int players)
    {
        var list = NeowRelics.Positive.ToList();
        if (NeowRelics.Twins.TryGetValue(cursed, out var twin)) list.Remove(twin);
        for (var coin = 0; coin < NeowRelics.Coins.Count; coin++)
        {
            if (coin == 0 && cursed == NeowRelics.NoFirstCoin) continue;
            var (heads, tails) = NeowRelics.Coins[coin];
            list.Add(rng.NextBool() ? heads : tails);
        }
        list.RemoveAll(relic => !NeowRelics.IsAllowed(relic, unlocks, players));
        rng.Shuffle(list);
        return list;
    }
}

/// <summary>
/// Neow, the Ancient of a first act's start point (the game's Neow): the run begins in her
/// room. The player is put at 0 HP and healed to full, three relics are offered
/// (<see cref="NeowOffer"/>), exactly one is taken, its pickup effect runs, and the room's
/// leave option opens the map.
/// <code>
/// // In the act's director, after MapState.BeginAct and map.Enter(run, map.Map.Start.Coord):
/// EventScreen.Open(run, rooms.Ancient!);                // "Neow"
/// MapState.MarkRoomVisited(run, RoomKind.Event);        // her room counts as an event room
/// </code>
/// <para>
/// The option's title and text are the relic's own name and text; the label reads "Take
/// &lt;relic&gt;". Neow has no page text in the game: the mod shows a line of her dialogue there,
/// and nothing once a relic was taken. The first page's text here is the arena's own.
/// </para>
/// <para>
/// Left out, by name: run modifiers (a daily or custom run, where Neow offers the modifiers'
/// own options instead); a profile that has not revealed Neow's epoch (it starts with a fight);
/// more than one player (each has an offer of their own, on a stream of their slot).
/// </para>
/// </summary>
public class Neow : AncientEvent
{
    public override string Title => "Neow";

    protected override bool HealsFromZero => true;

    protected override string InitialDescription(Run run) => "Neow offers three relics: two blessings, and a third that comes with a price. Take one.";

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run)
    {
        var state = run.State;
        var strict = state.FindPart<LeftOutLog>()?.Strict ?? false;
        var offer = NeowOffer.Roll(Rng, state.Part<RunWorld>().Unlocks, IsBuilt, strict);
        if (!offer.Found) throw new RunUnrepresentableException(offer.Reason!);
        foreach (var relic in offer.PassedOver) state.Part<LeftOutLog>().PassedOver(state, "relic", relic, "Neow's offer");
        return offer.Relics.Select(RelicOption).ToList();
    }
}

/// <summary>Neow is an event of the registry like any other: <c>EventScreen.Open(run, "Neow")</c>.</summary>
public sealed class NeowContent : IContentModule
{
    public void Register(Registry r) => r.Event<Neow>();
}
