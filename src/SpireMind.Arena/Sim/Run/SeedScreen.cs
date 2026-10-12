using System.Globalization;

namespace SpireMind.Arena;

/// <summary>One end of a relic bag's list of one rarity, in the order pulls take from it.</summary>
/// <param name="Bag">"player" or "shared".</param>
/// <param name="End">"front" (elites, events, a chest) or "back" (shops; without the relics a shop never sells).</param>
/// <param name="Rarity">The list's rarity.</param>
/// <param name="Relics">The list from that end, the relic pulled first at index 0.</param>
public sealed record BagEnd(string Bag, string End, RelicRarity Rarity, IReadOnlyList<string> Relics)
{
    public override string ToString() => $"the {End} of the {Bag} bag's {Rarity} list";
}

/// <summary>
/// What a seed fixes before anything is played, as far as the seed screen looks: the game's own
/// things, built or not. Everything here follows from the seed and the profile alone
/// (<see cref="Of"/> plays nothing and makes no run).
/// </summary>
/// <param name="Seed">The seed, as the game writes it.</param>
/// <param name="Act">The first act the game rolls for it ("Overgrowth" or "Underdocks").</param>
/// <param name="Neow">Neow's offer: two positive relics, then the cursed one.</param>
/// <param name="Events">The events of the act's list in the order the Event rooms walk it, from the entry after Neow's and
/// around: only the ones a first act can hold at all. Which of them an Event room takes also depends on the player then
/// (gold, HP, the deck), so the k-th room takes the k-th entry or a later one. Empty for an act that is not Overgrowth.</param>
/// <param name="BagEnds">Both ends of the player's bag and the front of the shared bag, a list per rarity a pull can ask for.</param>
/// <param name="ShopColorless">The two colorless cards of the run's first shop, the Uncommon one and the Rare one: the 13th and
/// the 15th number of the player's shops stream, whenever that shop is met, for a player who holds nothing that changes a
/// shop's cards.</param>
public sealed record SeedLook(string Seed, string Act, IReadOnlyList<string> Neow, IReadOnlyList<string> Events,
    IReadOnlyList<BagEnd> BagEnds, IReadOnlyList<string> ShopColorless)
{
    /// <summary>The act is the one first act the arena has built.</summary>
    public bool IsOvergrowth => Act == GameLists.Overgrowth;

    /// <summary>The numbers of the shops stream a shop draws before its first colorless card: the sale, five picks, six prices.</summary>
    public const int ShopNumbersBeforeColorless = 12;

    /// <summary>The look at a seed for a profile (everything unlocked when left out).</summary>
    public static SeedLook Of(string seed, RunUnlocks? unlocks = null)
    {
        unlocks ??= RunUnlocks.All;
        var streams = new RunStreams(seed);
        var start = RunGeneration.Start(streams, unlocks);
        var act = start.Acts[0];

        // Neow's stream is the event's own, the same whenever her room is entered.
        var neow = NeowOffer.OfTheGame(streams, unlocks);

        // The walk of the first Event room starts at entry 1: Neow's room has moved the counter (plan 0.1, W1.3).
        var events = new List<string>();
        if (act == GameLists.Overgrowth)
        {
            var list = start.Rooms[0].Events;
            for (var step = 0; step < list.Count; step++)
            {
                var entry = list[(1 + step) % list.Count];
                if (EventList.InAnOvergrowthAct.Contains(entry)) events.Add(entry);
            }
        }

        // The bags as the run starts with them. A relic the run does not allow is dropped at a pull; at the start of a
        // run that is Lasting Candy alone, for a profile that has not finished a run.
        var state = new RunState(new RunSetup { Seed = streams.Seed.Text });
        bool Allowed(string relic) => RunRelics.IsAllowed(relic, state, unlocks);
        var ends = new List<BagEnd>();
        RelicRarity[] rolled = [RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare];
        foreach (var rarity in rolled)
            ends.Add(new BagEnd("player", "front", rarity, start.PlayerBag.Relics(rarity).Where(Allowed).ToList()));
        foreach (var rarity in RelicBag.PlayerRarities)
            ends.Add(new BagEnd("player", "back", rarity, start.PlayerBag.Relics(rarity).Where(Allowed).Where(relic => !RunRelics.NeverInShops.Contains(relic)).Reverse().ToList()));
        foreach (var rarity in rolled)
            ends.Add(new BagEnd("shared", "front", rarity, start.SharedBag.Relics(rarity).Where(Allowed).ToList()));

        // The first shop's colorless cards: one pick each from the unlocked single-player cards of the rarity, in the
        // pool's order, with the first card's price between the two picks.
        var shops = streams.Long(RunStream.Shops).Clone();
        for (var n = 0; n < ShopNumbersBeforeColorless; n++) shops.NextDouble();
        var pool = CardPools.Colorless.Unlocked(unlocks);
        var colorless = new List<string>();
        foreach (var rarity in ShopInventory.ColorlessCardRarities)
        {
            if (colorless.Count > 0) shops.NextDouble();
            var options = pool.Where(card => card.Rarity == rarity).ToList();
            colorless.Add(options[shops.NextInt(0, options.Count)].ClassName);
        }

        return new SeedLook(streams.Seed.Text, act, neow, events, ends, colorless);
    }

    /// <summary>How many of the first events of the walk are built before the first that is not.</summary>
    public int EventsBuilt(Func<string, bool>? isBuilt = null) => Events.TakeWhile(isBuilt ?? EventList.IsBuilt).Count();

    /// <summary>How deep every bag end is built: the smallest count, over the ends, of relics at the end before the first that is not built.</summary>
    public int BagEndsBuilt(Func<string, bool>? isBuilt = null) =>
        BagEnds.Count == 0 ? 0 : BagEnds.Min(end => end.Relics.TakeWhile(isBuilt ?? RunRelics.IsBuilt).Count());
}

/// <summary>
/// The seed screen (plan 5.1, "screened seeds"): which seeds a run is started on when it should
/// begin as the game's own run for its seed, so that the same seed typed into the game gives the
/// same map, the same offer at Neow and the same first rooms. A seed passes when its act is
/// Overgrowth and what the seed fixes is built, as far as the screen is set to look:
/// <list type="bullet">
/// <item><see cref="Neow"/>: all three relics of Neow's offer.</item>
/// <item><see cref="Events"/>: the first that many events of the act's list in walk order.</item>
/// <item><see cref="BagEnds"/>: the first that many relics of every rarity at the front and the
/// back of the player's bag and at the front of the shared bag (an elite's and an event's relic,
/// a shop's three, a chest's).</item>
/// <item><see cref="ShopColorless"/>: the two colorless cards of the first shop.</item>
/// </list>
/// <para>
/// The screen costs a few list lookups a seed and plays nothing. A run that goes deeper than
/// its screen looked draws again from there (<see cref="RunMode.Screened"/>), and its log says
/// from which floor (<see cref="LeftOutLog.FirstFloor"/>).
/// </para>
/// <para>
/// The default looks at Neow's offer and the first event. The plan's screen (<see cref="Full"/>:
/// six events, every bag end, the shop's colorless cards) passes no seed while few relics are
/// built: with 25 of the bags' 122 relics about one Overgrowth seed in five million has all ten
/// bag ends built. Each test is a switch, so the screen can be made as deep as the content allows.
/// </para>
/// </summary>
/// <param name="Neow">Neow's three relics must be built.</param>
/// <param name="Events">How many of the first events of the walk must be built.</param>
/// <param name="BagEnds">How many relics deep every bag end must be built.</param>
/// <param name="ShopColorless">The first shop's two colorless cards must be built.</param>
public sealed record SeedScreen(bool Neow = true, int Events = 1, int BagEnds = 0, bool ShopColorless = false)
{
    /// <summary>Neow's offer and the first event.</summary>
    public static SeedScreen Default { get; } = new();

    /// <summary>The act alone: any seed whose first act is Overgrowth.</summary>
    public static SeedScreen ActOnly { get; } = new(false, 0, 0, false);

    /// <summary>The screen of the plan: Neow's offer, six events, every bag end one deep, the first shop's colorless cards.</summary>
    public static SeedScreen Full { get; } = new(true, 6, 1, true);

    /// <summary>
    /// Why a seed does not pass, in one line that names the first thing in the way; null when it passes.
    /// </summary>
    /// <param name="relicBuilt">Which relics the arena holds; the registry when left out. For checks.</param>
    /// <param name="eventBuilt">Which events; the registry when left out.</param>
    /// <param name="cardBuilt">Which cards; the registry when left out.</param>
    public string? WhyNot(SeedLook look, Func<string, bool>? relicBuilt = null, Func<string, bool>? eventBuilt = null, Func<string, bool>? cardBuilt = null)
    {
        relicBuilt ??= RunRelics.IsBuilt;
        eventBuilt ??= EventList.IsBuilt;
        cardBuilt ??= CardPools.IsBuilt;

        if (!look.IsOvergrowth)
            return $"the first act of seed {look.Seed} is {look.Act}; the arena has built {GameLists.Overgrowth} alone";
        if (Neow && look.Neow.FirstOrDefault(relic => !relicBuilt(relic)) is { } relic)
            return $"Neow offers {string.Join(", ", look.Neow)} on seed {look.Seed}, and the arena leaves out {relic}";
        if (Events > 0)
        {
            var first = look.Events.Take(Events).ToList();
            if (first.FirstOrDefault(name => !eventBuilt(name)) is { } missing)
                return string.Create(CultureInfo.InvariantCulture,
                    $"the first {Count(Events, "event")} of seed {look.Seed} {(first.Count == 1 ? "is" : "are")} {string.Join(", ", first)}, and the arena leaves out {missing}");
        }
        if (BagEnds > 0)
        {
            foreach (var end in look.BagEnds)
            {
                if (end.Relics.Take(BagEnds).FirstOrDefault(name => !relicBuilt(name)) is { } unbuilt)
                    return $"{end} of seed {look.Seed} holds {unbuilt}, which the arena leaves out";
            }
        }
        if (ShopColorless && look.ShopColorless.FirstOrDefault(card => !cardBuilt(card)) is { } card)
            return $"the first shop of seed {look.Seed} sells the colorless cards {string.Join(" and ", look.ShopColorless)}, and the arena leaves out {card}";
        return null;
    }

    /// <summary><see cref="WhyNot(SeedLook, Func{string, bool}?, Func{string, bool}?, Func{string, bool}?)"/> for a seed and a profile.</summary>
    public string? WhyNot(string seed, RunUnlocks? unlocks = null) => WhyNot(SeedLook.Of(seed, unlocks));

    public bool Passes(string seed, RunUnlocks? unlocks = null) => WhyNot(seed, unlocks) is null;

    /// <summary>
    /// A screen from its text form, as a command line gives it: "default", "act", "full", or the
    /// tests by name separated by commas: "neow", "events=2", "bags=1", "shop" (what is not named
    /// is off). <see cref="ToString"/> writes the same form.
    /// </summary>
    /// <exception cref="FormatException">The text names no screen.</exception>
    public static SeedScreen Parse(string text)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "default": return Default;
            case "act": return ActOnly;
            case "full": return Full;
        }

        var screen = ActOnly;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=');
            var name = pair[0].Trim().ToLowerInvariant();
            int Depth() => pair.Length == 2 && int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 0 and <= 31
                ? n
                : throw new FormatException($"'{part}' needs a depth from 0 to 31, as in {name}=1.");
            switch (name)
            {
                case "neow" when pair.Length == 1: screen = screen with { Neow = true }; break;
                case "shop" when pair.Length == 1: screen = screen with { ShopColorless = true }; break;
                case "events": screen = screen with { Events = Depth() }; break;
                case "bags": screen = screen with { BagEnds = Depth() }; break;
                default: throw new FormatException($"'{part}' is no test of the seed screen: use default, act, full, or any of neow, events=N, bags=N, shop.");
            }
        }
        return screen;
    }

    /// <summary>The screen in the form <see cref="Parse"/> reads.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Neow) parts.Add("neow");
        if (Events > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"events={Events}"));
        if (BagEnds > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"bags={BagEnds}"));
        if (ShopColorless) parts.Add("shop");
        return parts.Count == 0 ? "act" : string.Join(",", parts);
    }

    /// <summary>The screen in words, for a report.</summary>
    public string Describe()
    {
        var parts = new List<string> { "the act is Overgrowth" };
        if (Neow) parts.Add("Neow's three relics are built");
        if (Events > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"the first {Count(Events, "event")} of the list {(Events == 1 ? "is" : "are")} built"));
        if (BagEnds > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"every end of the relic bags is built {BagEnds} deep"));
        if (ShopColorless) parts.Add("the first shop's two colorless cards are built");
        return string.Join("; ", parts);
    }

    private static string Count(int n, string noun) => string.Create(CultureInfo.InvariantCulture, $"{n} {noun}{(n == 1 ? "" : "s")}");
}
