using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The run self-check: the act as one director (<see cref="ActDirector"/>), its floor log, the
/// seed screen, the three modes, the scripted run players and <c>run-baseline</c>.
/// <para>
/// What is held against the real game: the seed screen's look at nine real seeds (the act, Neow's
/// offer, the order of the event list, the ends of both relic bags and the first shop's colorless
/// cards, each typed in from Mark's saves and the game's own run records in
/// sts2-notes/golden-saves), and the real run on seed 1BUSLN0FY5 played through the director
/// with its floor log compared to the game's record floor by floor (history/1791755111.run).
/// What is worked out by hand: the arithmetic of the share of real runs, the players' plain
/// rules, the list of choices a run keeps.
/// </para>
/// <para>
/// Then the soak: scripted players walk whole acts in every mode, and every run must be played
/// without an action turned down, end in a way the mode allows, show nothing that is not
/// built, show no shop with an empty place, replay from its action list to the same state and
/// the same floor log, go on the same from a copy taken on the way, and be carried on from its
/// save on disk. Strict runs on seeds as they come give the share of real runs the build can
/// represent, which is printed. <c>ARENA_RUN_SOAK=3000</c> walks thousands of acts in the place
/// of the few hundred of every self-check.
/// </para>
/// <para>
/// The checks hold for whatever the registry holds. Where a group needs something the registry
/// may lack (Neow's relics of the real run, the event This or That), it says what it found
/// instead and checks that: a run that cannot begin must end as unrepresentable with the
/// reason, never fail.
/// </para>
/// </summary>
public sealed class RunLoopChecks : IArenaChecks
{
    private const string PlayedSeed = "1BUSLN0FY5";

    public void Run(Check c)
    {
        TheSeedScreen(c);
        TheStart(c);
        TheRealRun(c);
        TheRealRunStrict(c);
        TheChoices(c);
        TheShareArithmetic(c);
        ThePlayers(c);
        WholeActs(c);
        TheStrictShare(c);
        TheCommand(c);
    }

    // ---------- helpers ----------

    private static readonly IPolicy Greedy = Policies.All.Single(policy => policy.Name == "Greedy");

    /// <summary>The soak's size: the number the environment asks for, or 0.</summary>
    private static int Soak => int.TryParse(Environment.GetEnvironmentVariable("ARENA_RUN_SOAK"), out var asked) && asked > 0 ? asked : 0;

    private static readonly int Threads = Math.Max(1, Math.Min(4, Environment.ProcessorCount));

    /// <summary>Plays the fight that is on to its end.</summary>
    private static void PlayFight(Arena.Run run) => RunPlayers.PlayFight(run, Greedy);

    /// <summary>Takes the action the snapshot offers under a label; the labels are the mod's, so a wrong one fails the group.</summary>
    private static void Take(Arena.Run run, string label)
    {
        var s = run.Snapshot();
        var action = s.Actions.FirstOrDefault(a => a.Label == label)
            ?? throw new InvalidOperationException($"No action is labelled '{label}' on {s.Screen}: {Labels(s)}");
        Do(run, action.Id);
    }

    private static string Lines(IEnumerable<RunChoice> choices) => string.Join(" | ", choices.Select(choice => $"{choice.Screen} {choice.Id} {choice.Label}"));

    private static IReadOnlyList<LeftOutLog.Entry> Logged(Arena.Run run) => run.State.FindPart<LeftOutLog>()?.Entries ?? [];

    /// <summary>Whether Neow can make an offer at all from what the registry holds: a cursed relic and two positive ones.</summary>
    private static bool NeowCanOffer =>
        NeowRelics.Cursed.Any(RunRelics.IsBuilt)
        && NeowRelics.Positive.Concat(NeowRelics.Coins.SelectMany(pair => new[] { pair.Heads, pair.Tails })).Count(RunRelics.IsBuilt) >= 2;

    /// <summary>Where two texts part, with a little of both around it.</summary>
    private static string Difference(string a, string b)
    {
        var at = 0;
        while (at < a.Length && at < b.Length && a[at] == b[at]) at++;
        static string Around(string text, int at) => text[Math.Max(0, at - 120)..Math.Min(text.Length, at + 100)];
        return $" (from character {at}: [{Around(a, at)}] against [{Around(b, at)}])";
    }

    // ---------- the seed screen ----------

    /// <summary>What a real seed fixes, typed in from the game's own files: (bag, end, rarity) and the first two relics a pull takes there.</summary>
    private static readonly (string Seed, (string Bag, string End, RelicRarity Rarity, string First, string Second)[] Ends, string[] Events)[] SavedSeeds =
    [
        // golden-saves/run-164514-abe3bfe862ef.save: players[0].relic_grab_bag, shared_relic_grab_bag, acts[0].rooms.event_ids.
        // The Uncommon list of the player's bag ends Paper Phrog, Lucky Fysh, Bowler Hat and the Rare list Gambling Chip, The
        // Courier, Toxic Egg: a shop never sells Lucky Fysh, Bowler Hat or The Courier.
        ("1BUSLN0FY5",
        [
            ("player", "front", RelicRarity.Common, "RegalPillow", "Strawberry"), ("player", "front", RelicRarity.Uncommon, "StoneCracker", "ParryingShield"),
            ("player", "front", RelicRarity.Rare, "Shuriken", "RainbowRing"),
            ("player", "back", RelicRarity.Common, "Pendulum", "Anchor"), ("player", "back", RelicRarity.Uncommon, "PaperPhrog", "SparklingRouge"),
            ("player", "back", RelicRarity.Rare, "ToxicEgg", "GamblingChip"), ("player", "back", RelicRarity.Shop, "RingingTriangle", "Orrery"),
            ("shared", "front", RelicRarity.Common, "FestivePopper", "StrikeDummy"), ("shared", "front", RelicRarity.Uncommon, "Akabeko", "MiniatureCannon"),
            ("shared", "front", RelicRarity.Rare, "TungstenRod", "BeatingRemnant"),
        ],
        // The list starts The Future of Potions, This or That, Sapphire Seed, Unrest Site, Ranwid the Elder, War Historian Repy,
        // Self Help Book, Wood Carvings: entry 0 is Neow's, and Ranwid and Repy cannot come up in a first act.
        ["ThisOrThat", "SapphireSeed", "UnrestSite", "SelfHelpBook", "WoodCarvings"]),

        // golden-saves/run-164206-14bb2d424ffb.save. The list starts Self Help Book, Fake Merchant, Morphic Grove, Unrest Site,
        // Wellspring, Relic Trader, War Historian Repy, Jungle Maze Adventure.
        ("KMEHXZW0R5",
        [
            ("player", "front", RelicRarity.Common, "BagOfMarbles", "BookOfFiveRings"), ("player", "front", RelicRarity.Uncommon, "TuningFork", "LastingCandy"),
            ("player", "front", RelicRarity.Rare, "Girya", "TungstenRod"),
            ("player", "back", RelicRarity.Common, "BronzeScales", "MealTicket"), ("player", "back", RelicRarity.Uncommon, "RippleBasin", "Orichalcum"),
            ("player", "back", RelicRarity.Rare, "ArtOfWar", "MoltenEgg"), ("player", "back", RelicRarity.Shop, "LavaLamp", "ChemicalX"),
            ("shared", "front", RelicRarity.Common, "AmethystAubergine", "RegalPillow"), ("shared", "front", RelicRarity.Uncommon, "PenNib", "Pantograph"),
            ("shared", "front", RelicRarity.Rare, "LizardTail", "CaptainsWheel"),
        ],
        ["MorphicGrove", "UnrestSite", "Wellspring", "JungleMazeAdventure"]),

        // golden-saves/run-164247-76723b820cae.save. The list starts Sapphire Seed, Stone of All Time, War Historian Repy, Room
        // Full of Cheese, Self Help Book, Byrdonis Nest, Aroma of Chaos, Symbiote. The Uncommon list ends Vambrace, Lucky Fysh, Permafrost.
        ("EEXRY5WA1R",
        [
            ("player", "front", RelicRarity.Common, "JuzuBracelet", "BookOfFiveRings"), ("player", "front", RelicRarity.Uncommon, "StoneCracker", "MercuryHourglass"),
            ("player", "front", RelicRarity.Rare, "RuinedHelmet", "UnceasingTop"),
            ("player", "back", RelicRarity.Common, "VenerableTeaSet", "RedSkull"), ("player", "back", RelicRarity.Uncommon, "Permafrost", "Vambrace"),
            ("player", "back", RelicRarity.Rare, "FrozenEgg", "Bellows"), ("player", "back", RelicRarity.Shop, "MiniatureTent", "TheAbacus"),
            ("shared", "front", RelicRarity.Common, "Anchor", "BagOfMarbles"), ("shared", "front", RelicRarity.Uncommon, "MercuryHourglass", "ParryingShield"),
            ("shared", "front", RelicRarity.Rare, "Mango", "CloakClasp"),
        ],
        ["RoomFullOfCheese", "SelfHelpBook", "ByrdonisNest", "AromaOfChaos"]),

        // golden-saves/run-164409-761d393b4972.save. The list starts Crystal Sphere, The Legends Were True, Stone of All Time,
        // Ranwid the Elder, Sapphire Seed, Tablet of Truth, Sunken Statue, Tea Master. The Rare list ends Intimidating Helmet,
        // The Courier, Beating Remnant.
        ("E9U400ERJ4",
        [
            ("player", "front", RelicRarity.Common, "OddlySmoothStone", "Vajra"), ("player", "front", RelicRarity.Uncommon, "Kusarigama", "ReptileTrinket"),
            ("player", "front", RelicRarity.Rare, "DemonTongue", "CloakClasp"),
            ("player", "back", RelicRarity.Common, "RedSkull", "VenerableTeaSet"), ("player", "back", RelicRarity.Uncommon, "GremlinHorn", "Pear"),
            ("player", "back", RelicRarity.Rare, "BeatingRemnant", "IntimidatingHelmet"), ("player", "back", RelicRarity.Shop, "BurningSticks", "Kifuda"),
            ("shared", "front", RelicRarity.Common, "MealTicket", "BagOfMarbles"), ("shared", "front", RelicRarity.Uncommon, "ReptileTrinket", "Pantograph"),
            ("shared", "front", RelicRarity.Rare, "MummifiedHand", "Chandelier"),
        ],
        ["TheLegendsWereTrue", "SapphireSeed", "TabletOfTruth", "SunkenStatue", "TeaMaster"]),
    ];

    /// <summary>The first act and Neow's offer of real single-player runs, as the game's records of them have it (golden-saves/history).</summary>
    private static readonly (string Seed, string Act, string[] Neow)[] RecordedStarts =
    [
        ("1BUSLN0FY5", "Overgrowth", ["PreciseScissors", "ArcaneScroll", "LeafyPoultice"]),   // 1791755111.run
        ("3FBTGQ63TM", "Overgrowth", ["StoneHumidifier", "NewLeaf", "CursedPearl"]),          // 1791596381.run
        ("AG5XPLQQNY", "Overgrowth", ["PhialHolster", "GoldenPearl", "SilkenTress"]),         // 1791597100.run
        ("75F277YN2G", "Underdocks", ["Pomander", "NutritiousOyster", "SilverCrucible"]),     // 1791592972.run
        ("1CJXGGA4E5", "Underdocks", ["NeowsTorment", "LavaRock", "CursedPearl"]),            // 1791585653.run
    ];

    private static void TheSeedScreen(Check c)
    {
        c.Group("the seed screen looks at real seeds and finds what the game's saves and records hold", () =>
        {
            foreach (var (seed, ends, events) in SavedSeeds)
            {
                var look = SeedLook.Of(seed);
                c.Equal("Overgrowth", look.Act, $"{seed}: the first act of the save");
                c.That(look.IsOvergrowth, $"{seed}: which is the act the arena has built");
                c.Sequence(events, look.Events.Take(events.Length), $"{seed}: the saved event list in the order the Event rooms walk it, from the entry after Neow's, without the events no first act can hold");
                c.That(look.Events.All(EventList.InAnOvergrowthAct.Contains) && look.Events.Count == look.Events.Distinct().Count(), $"{seed}: every event of the walk can come up in an Overgrowth act, none twice");
                c.Equal(10, look.BagEnds.Count, $"{seed}: ten ends: three fronts and four backs of the player's bag, three fronts of the shared bag");
                foreach (var (bag, end, rarity, first, second) in ends)
                {
                    var found = look.BagEnds.Single(e => e.Bag == bag && e.End == end && e.Rarity == rarity);
                    c.Sequence([first, second], found.Relics.Take(2), $"{seed}: {found}, as the save has it");
                }
            }

            foreach (var (seed, act, neow) in RecordedStarts)
            {
                var look = SeedLook.Of(seed);
                c.Equal(act, look.Act, $"{seed}: the first act of the game's record");
                c.Sequence(neow, look.Neow, $"{seed}: Neow's offer of the record, in its order");
                if (act != "Overgrowth") c.Equal(0, look.Events.Count, $"{seed}: no event walk for an act the arena has not built");
            }

            // The seed of the save F30CH975SF starts in Underdocks (golden-saves/run-164306-b9fa0c83f2b6.save, README).
            c.Equal("Underdocks", SeedLook.Of("F30CH975SF").Act, "F30CH975SF: Underdocks");

            // The first shop of two real single-player runs, whose records list the shelf: Discovery and Calamity on floor 5 of
            // seed 1BUSLN0FY5 (after two fights and an event), Jack of All Trades and Secret Technique on floor 3 of seed 75F277YN2G
            // (after one fight). Whenever the first shop is met, its colorless cards are the 13th and 15th number of shops.
            c.Sequence(["Discovery", "Calamity"], SeedLook.Of("1BUSLN0FY5").ShopColorless, "1BUSLN0FY5: the first shop's colorless cards of the record");
            c.Sequence(["JackOfAllTrades", "SecretTechnique"], SeedLook.Of("75F277YN2G").ShopColorless, "75F277YN2G: the first shop's colorless cards of the record");
            c.Equal(12, SeedLook.ShopNumbersBeforeColorless, "a shop draws 12 numbers of shops before them: the sale, five picks, five prices and the sale's second price");
        });

        c.Group("the seed screen passes a seed only when what it is set to look at is built", () =>
        {
            var look = SeedLook.Of(PlayedSeed);
            static bool All(string _) => true;
            static Func<string, bool> Without(params string[] names) => name => !names.Contains(name);

            c.Equal(null, SeedScreen.Full.WhyNot(look, All, All, All), "with everything built the real seed passes the plan's whole screen");
            c.Equal(null, SeedScreen.Default.WhyNot(look, All, All, All), "and the default one");

            // Neow: all three relics of the offer.
            var why = SeedScreen.Default.WhyNot(look, Without("LeafyPoultice"), All, All);
            c.Equal("Neow offers PreciseScissors, ArcaneScroll, LeafyPoultice on seed 1BUSLN0FY5, and the arena leaves out LeafyPoultice", why, "without the cursed relic of the offer the default screen says no and names it");
            c.Equal(null, SeedScreen.ActOnly.WhyNot(look, Without("LeafyPoultice"), All, All), "the act-only screen does not look at Neow");

            // Events: the first N of the walk (This or That, Sapphire Seed, Unrest Site, ...).
            c.Equal("the first 1 event of seed 1BUSLN0FY5 is ThisOrThat, and the arena leaves out ThisOrThat",
                SeedScreen.Default.WhyNot(look, All, Without("ThisOrThat"), All), "without the first event the default screen says no");
            c.Equal(null, SeedScreen.Default.WhyNot(look, All, Without("SapphireSeed"), All), "the second event is not looked at by a screen one event deep");
            c.Equal("the first 2 events of seed 1BUSLN0FY5 are ThisOrThat, SapphireSeed, and the arena leaves out SapphireSeed",
                (SeedScreen.Default with { Events = 2 }).WhyNot(look, All, Without("SapphireSeed"), All), "but by one that is two deep");
            c.Equal(2, look.EventsBuilt(name => name is "ThisOrThat" or "SapphireSeed" or "SelfHelpBook"), "two events are built at the front when the third, Unrest Site, is not");

            // Bag ends: the front of the player's Common list is Regal Pillow; its back is Pendulum, then Anchor.
            var bags1 = SeedScreen.ActOnly with { BagEnds = 1 };
            var bags2 = SeedScreen.ActOnly with { BagEnds = 2 };
            c.Equal("the front of the player bag's Common list of seed 1BUSLN0FY5 holds RegalPillow, which the arena leaves out", bags1.WhyNot(look, Without("RegalPillow"), All, All), "a bag end one deep");
            c.Equal(null, bags1.WhyNot(look, Without("Anchor"), All, All), "Anchor is second from the back of the Common list: one deep does not reach it");
            c.Equal("the back of the player bag's Common list of seed 1BUSLN0FY5 holds Anchor, which the arena leaves out", bags2.WhyNot(look, Without("Anchor"), All, All), "two deep does");
            c.Equal(null, bags2.WhyNot(look, Without("LuckyFysh", "BowlerHat", "TheCourier"), All, All), "the relics a shop never sells are not at the back as far as a shop is concerned");
            c.Equal(0, look.BagEndsBuilt(Without("RegalPillow")), "no end is built deeper than the shallowest one");
            c.Equal(1, look.BagEndsBuilt(Without("Anchor")), "one deep when Anchor, second at one end, is missing");
            c.Equal("the front of the shared bag's Rare list of seed 1BUSLN0FY5 holds TungstenRod, which the arena leaves out", bags1.WhyNot(look, Without("TungstenRod"), All, All), "the shared bag's fronts are looked at too (a chest)");

            // The first shop's colorless cards.
            var shop = SeedScreen.ActOnly with { ShopColorless = true };
            c.Equal("the first shop of seed 1BUSLN0FY5 sells the colorless cards Discovery and Calamity, and the arena leaves out Calamity", shop.WhyNot(look, All, All, Without("Calamity")), "without the Rare colorless card of the first shop");
            c.Equal(null, SeedScreen.Default.WhyNot(look, All, All, Without("Calamity")), "which the default screen does not look at");

            // Another first act, whatever is built.
            c.Equal("the first act of seed 75F277YN2G is Underdocks; the arena has built Overgrowth alone", SeedScreen.ActOnly.WhyNot(SeedLook.Of("75F277YN2G"), All, All, All), "an Underdocks seed passes no screen");

            // The text form.
            c.Equal("neow,events=1", SeedScreen.Default.ToString(), "the default screen in words a command line takes");
            c.Equal("act", SeedScreen.ActOnly.ToString(), "the act-only screen");
            c.Equal("neow,events=6,bags=1,shop", SeedScreen.Full.ToString(), "the plan's screen");
            foreach (var screen in new[] { SeedScreen.Default, SeedScreen.ActOnly, SeedScreen.Full, new SeedScreen(false, 3, 2, true) })
                c.Equal(screen, SeedScreen.Parse(screen.ToString()), $"'{screen}' reads back as the same screen");
            c.Equal(SeedScreen.Default, SeedScreen.Parse("default"), "'default'");
            c.Equal(SeedScreen.Full, SeedScreen.Parse("full"), "'full'");
            foreach (var bad in new[] { "bogus", "events", "events=x", "bags=-1", "neow=1" })
            {
                var refused = false;
                try { SeedScreen.Parse(bad); }
                catch (FormatException) { refused = true; }
                c.That(refused, $"'{bad}' names no screen");
            }

            // The registry's own answer is the same question asked of the registry.
            c.Equal(SeedScreen.Default.WhyNot(look) is null, SeedScreen.Default.Passes(PlayedSeed), "Passes is WhyNot with what the registry holds");
        });

        c.Group("seeds for a mode: every seed for a strict run, Overgrowth seeds for drawing again, seeds that pass for a screened run", () =>
        {
            var all = ActRun.RandomSeeds(7).Take(200).ToList();
            c.Sequence(all, ActRun.RandomSeeds(7).Take(200), "the same maker seed gives the same seeds");
            c.That(!all.SequenceEqual(ActRun.RandomSeeds(8).Take(200)), "another maker seed gives others");
            c.That(all.All(seed => seed.Length == RunSeed.DefaultLength && seed.All(RunSeed.Alphabet.Contains)), "ten letters and digits of the game's alphabet each");
            c.Sequence(all.Take(50), ActRun.Seeds(50, 7, RunMode.Strict, out var triedStrict), "a strict run takes the seeds as they come");
            c.Equal(50, triedStrict, "none is passed over");

            var overgrowth = all.Where(seed => SeedLook.Of(seed).IsOvergrowth).ToList();
            // The act roll is a coin (plan 0, row 1): of 200 seeds between 72 and 128 are Overgrowth's with a chance of all but 1 in 15,000.
            c.That(overgrowth.Count is >= 72 and <= 128, $"about half of 200 seeds start in Overgrowth ({overgrowth.Count})");
            var drawAgain = ActRun.Seeds(overgrowth.Count, 7, RunMode.DrawAgain, out var triedAgain);
            c.Sequence(overgrowth, drawAgain, "drawing again takes exactly the Overgrowth seeds, in order");
            c.That(triedAgain <= 200 && all[triedAgain - 1] == overgrowth[^1], "and stops at the last one it needed");

            var screen = SeedScreen.ActOnly with { Events = 1 };
            var passing = all.Where(seed => screen.Passes(seed)).ToList();
            c.Sequence(passing, ActRun.Seeds(passing.Count, 7, RunMode.Screened, out _, screen), "a screened run takes the seeds that pass its screen, in order");
            c.Equal(0, ActRun.Seeds(3, 7, RunMode.Screened, out var gaveUp, SeedScreen.Full with { BagEnds = 40 }, mostTried: 300).Count, "a screen nothing passes finds no seed");
            c.Equal(300, gaveUp, "and gives up where it is told to");
        });
    }

    // ---------- the start of a run ----------

    private static void TheStart(Check c)
    {
        c.Group("a seed whose first act is not Overgrowth is refused at the start in every mode", () =>
        {
            // Three real seeds the game started in Underdocks (a save and two run records).
            foreach (var seed in new[] { "F30CH975SF", "75F277YN2G", "1CJXGGA4E5" })
            {
                foreach (var mode in new[] { RunMode.DrawAgain, RunMode.Screened, RunMode.Strict })
                {
                    var run = ActRun.Start(seed, mode);
                    c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, $"{seed}, {mode}: over at once as unrepresentable");
                    c.Equal($"the first act of seed {seed} is Underdocks; the arena has built Overgrowth alone", run.ResultReason, $"{seed}, {mode}: the reason names the act");
                    c.Equal(0, run.State.TotalFloor, $"{seed}, {mode}: no floor was entered");
                    c.Equal(0, run.Actions.Count + run.Choices.Count + FloorLog.Of(run).Count, $"{seed}, {mode}: no action, no choice, no floor in the log");
                    var s = run.Snapshot();
                    c.Equal("arena_over done unrepresentable", $"{s.Screen} {Ids(s)} {s.State["result"]}", $"{seed}, {mode}: the end snapshot");
                    var result = ActResult.Of(run, "nobody");
                    c.Equal(("act", (int?)0, false), (ActRun.MissingKind(result.Reason), result.MetFloor, result.Finished), $"{seed}, {mode}: counted as another act, met before floor 1");
                    c.Equal(run.Dump(), Arena.Run.Replay(ActRun.Setup(seed), ActRun.Director(mode), run.Actions).Dump(), $"{seed}, {mode}: a replay ends the same way");
                }
            }
        });

        c.Group("the modes: keyed streams only for drawing again, a screened run only on a seed that passes its screen", () =>
        {
            foreach (var mode in new[] { RunMode.Screened, RunMode.Strict })
            {
                var refused = false;
                try { ActRun.Start(PlayedSeed, mode, StreamMode.Keyed); }
                catch (ArgumentException) { refused = true; }
                c.That(refused, $"a {mode} run with keyed streams is refused: it has to be the game's own run");
                refused = false;
                try { _ = new Arena.Run(ActRun.Setup(PlayedSeed), new ActDirector { Mode = mode, Streams = StreamMode.Keyed }); }
                catch (ArgumentException) { refused = true; }
                c.That(refused, $"also when the director is made by hand ({mode})");
            }

            var director = ActRun.Director(RunMode.Screened, screen: SeedScreen.Full);
            var copy = (ActDirector)director.Clone(new CloneMap());
            c.Equal((RunMode.Screened, StreamMode.Game, SeedScreen.Full), (copy.Mode, copy.Streams, copy.Screen), "a director's copy keeps its mode, streams and screen");
            c.That(new ActDirector().KeepsChoices, "the act's director has its run keep the choices");
            c.Equal(RunSeed.Canonical(" 1busln0fy5 "), ActRun.Setup(" 1busln0fy5 ").Seed, "a seed is taken as the game writes it");

            // A screen the seed fails: the run is refused with the screen's own reason. (The plan's whole screen asks for Regal
            // Pillow at the front of the Common list of seed 1BUSLN0FY5, among others.)
            foreach (var screen in new[] { SeedScreen.Full, SeedScreen.Default, SeedScreen.ActOnly })
            {
                var why = screen.WhyNot(PlayedSeed);
                var run = ActRun.Start(PlayedSeed, RunMode.Screened, screen: screen);
                if (why is not null)
                {
                    c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, $"screen {screen}: the seed fails it, so the run is over at once");
                    c.Equal($"the seed does not pass the screen ({screen}): {why}", run.ResultReason, $"screen {screen}: with the screen's reason");
                    c.Equal(("screen", (int?)0), (ActRun.MissingKind(run.ResultReason), ActResult.Of(run, "nobody").MetFloor), $"screen {screen}: counted as refused by the screen");
                }
                else if (!NeowCanOffer)
                {
                    c.That(run.AtEnd && run.Result == RunResult.Unrepresentable && ActRun.MissingKind(run.ResultReason) == "neow", $"screen {screen}: the seed passes, and Neow has nothing built to offer ({run.ResultReason})");
                }
                else
                {
                    c.That(!run.IsOver && run.Snapshot().Screen == "event", $"screen {screen}: the seed passes, and the run begins in Neow's room");
                }
            }

            // Keyed streams reach the run's world.
            var keyed = ActRun.Start(PlayedSeed, RunMode.DrawAgain, StreamMode.Keyed);
            if (!keyed.IsOver) c.Equal(StreamMode.Keyed, keyed.State.FindPart<RunWorld>()!.Streams.Mode, "a keyed run's world has keyed streams");
            var game = ActRun.Start(PlayedSeed);
            if (!game.IsOver) c.Equal(StreamMode.Game, game.State.FindPart<RunWorld>()!.Streams.Mode, "any other run has the game's");
        });

        c.Group("an Unknown point is not rolled to be an Event room while no built event is allowed and unmet, except in a strict run", () =>
        {
            foreach (var mode in new[] { RunMode.DrawAgain, RunMode.Strict })
            {
                var run = ActRun.Start(PlayedSeed, mode);
                if (run.IsOver)
                {
                    c.That(run.Result == RunResult.Unrepresentable, $"{mode}: the run could not begin ({run.ResultReason}), so there is no point to roll");
                    continue;
                }
                var director = (IMapDirector)run.Director;
                foreach (var room in new[] { RolledRoom.Monster, RolledRoom.Elite, RolledRoom.Treasure, RolledRoom.Shop })
                    c.That(director.CanHold(run, room), $"{mode}: a {room} room can always be shown");
                c.Equal(EventList.CanHold(run), director.CanHold(run, RolledRoom.Event), $"{mode}: an Event room when the act's list has an event for it");

                // Every built event of the act's list met: nothing is left for an Event room.
                var met = run.State.Part<EventsState>();
                foreach (var name in EventList.InAnOvergrowthAct.Where(EventList.IsBuilt)) met.MarkVisited(name);
                if (mode == RunMode.Strict)
                    c.That(director.CanHold(run, RolledRoom.Event), "Strict: with every built event met, a strict run still takes the game's roll (and ends there if the event is not built)");
                else if (AlwaysAllowed.Any(name => !EventList.IsBuilt(name)))
                    // The game's walk then lands on an event that is always allowed and not built, and nothing built is left to take in its place.
                    c.That(!director.CanHold(run, RolledRoom.Event), "DrawAgain: with every built event met and an unbuilt one waiting, an Unknown point is rolled again with the Event barred");
                c.That(director.CanHold(run, RolledRoom.Monster), $"{mode}: and a Monster room can still be shown");
            }
        });

        c.Group("an Unknown point rolled again with the Event room barred is a redraw like any other: it is in the run's log, on the floor the strict run stops on", () =>
        {
            // Twins on the same seed with the same actions, one strict and one drawing again, with every built event marked as
            // met after Neow's room: from there no built event is left for an Event room. Where the game rolls an Unknown point
            // to be an Event room, the strict twin ends (the game's event is not built) and the other is rolled again.
            var walker = new RunWalker(11);
            var (barred, stoppedForAnEvent, problems) = (0, 0, new List<string>());
            foreach (var seed in ActRun.Seeds(30, 20261013, RunMode.DrawAgain))
            {
                var strict = ActRun.Start(seed, RunMode.Strict);
                var again = ActRun.Start(seed);
                if (strict.IsOver || again.IsOver) continue;
                var marked = false;
                for (var step = 0; step < RunPlayers.MostActions && !again.AtEnd && !strict.IsOver; step++)
                {
                    if (!marked && again.State.TotalFloor == 1 && again.Snapshot().Screen == "map")
                    {
                        marked = true;
                        foreach (var run in new[] { strict, again })
                            foreach (var name in EventList.InAnOvergrowthAct.Where(EventList.IsBuilt)) run.State.Part<EventsState>().MarkVisited(name);
                    }
                    var id = walker.Choose(again, again.Snapshot());
                    var (first, second) = (strict.Execute(id), again.Execute(id));
                    if (!first.Ok || !second.Ok) problems.Add($"{seed}: '{id}' was refused ({first.Error} {second.Error})");
                }
                // The twin that draws again plays on to its end: every event its log holds was passed over on an Unknown point's
                // floor, once for a point that became another room.
                for (var step = 0; step < RunPlayers.MostActions && !again.AtEnd; step++)
                    if (!again.Execute(walker.Choose(again, again.Snapshot())).Ok) break;
                var floors = FloorLog.Of(again);
                foreach (var entry in Logged(again).Where(entry => entry.Kind == "event"))
                {
                    var where = floors.FirstOrDefault(l => l.Floor == entry.Floor);
                    if (where is not { Point: "unknown" }) problems.Add($"{seed}: the event {entry.ClassName} was passed over on floor {entry.Floor}, which is no Unknown point ({where?.Point})");
                }
                foreach (var where in floors.Where(l => l.Point == "unknown" && l.Room != "Event"))
                    if (where.DrawnAgain.Count(entry => entry.StartsWith("event:", StringComparison.Ordinal)) > 1) problems.Add($"{seed}: floor {where.Floor} was rolled again once and lists {string.Join(", ", where.DrawnAgain)}");
                if (strict.Result != RunResult.Unrepresentable || ActRun.MissingKind(strict.ResultReason) != "event") continue;

                stoppedForAnEvent++;
                var floor = ActResult.Of(strict, "twin").MetFloor;
                var logged = Logged(again).Where(entry => entry.Floor == floor && entry.Kind == "event").ToList();
                var line = FloorLog.Of(again).FirstOrDefault(l => l.Floor == floor);
                if (logged.Count == 0) problems.Add($"{seed}: the strict twin stopped on floor {floor} ({strict.ResultReason}); the other twin's log has no event passed over on that floor");
                else if (!logged.Any(entry => strict.ResultReason!.Contains(entry.ClassName, StringComparison.Ordinal))) problems.Add($"{seed}: the strict twin stopped for '{strict.ResultReason}'; the other twin logged {string.Join(", ", logged.Select(entry => entry.ClassName))}");
                if (logged.Any(entry => EventList.IsBuilt(entry.ClassName))) problems.Add($"{seed}: an event that is built was logged as passed over on floor {floor}");
                if (line is { Point: "unknown" } && line.Room != "Event")
                {
                    barred++;
                    if (!line.DrawnAgain.Any(entry => entry.StartsWith("event:", StringComparison.Ordinal))) problems.Add($"{seed}: floor {floor} of the floor log does not show the event as drawn again");
                }
            }
            c.Equal("", string.Join(" | ", problems.Take(4)), $"{stoppedForAnEvent} strict twins stopped at an Event room; each time the other twin's log names the game's event on that floor ({problems.Count} problems)");
            // With an event that is always allowed and not built the game's walk lands on it whenever an Unknown point rolls an Event room.
            if (NeowCanOffer && AlwaysAllowed.Any(name => !EventList.IsBuilt(name)))
                c.That(barred > 0 && barred == stoppedForAnEvent, $"and each time the point was rolled again into another room, since no built event was left ({barred} of {stoppedForAnEvent})");
        });

        c.Group("a run begins in Neow's room on floor 1, or ends there as unrepresentable when she has nothing built to offer", () =>
        {
            var look = SeedLook.Of(PlayedSeed);
            foreach (var mode in new[] { RunMode.DrawAgain, RunMode.Strict })
            {
                var run = ActRun.Start(PlayedSeed, mode);
                var gamesOfferBuilt = look.Neow.All(RunRelics.IsBuilt);
                if (mode == RunMode.Strict ? !gamesOfferBuilt : !NeowCanOffer)
                {
                    c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, $"{mode}: Neow's offer cannot be shown, so the run is over at once");
                    c.Equal("neow", ActRun.MissingKind(run.ResultReason), $"{mode}: because of Neow ({run.ResultReason})");
                    c.Equal((int?)1, ActResult.Of(run, "nobody").MetFloor, $"{mode}: met on floor 1, Neow's room");
                    c.Equal(0, run.State.TotalFloor, $"{mode}: standing as before the run began");
                    continue;
                }

                var s = run.Snapshot();
                c.Equal("event", s.Screen, $"{mode}: the first screen is an event's");
                c.Equal("Neow", s.State["event"]!.GetValue<string>(), $"{mode}: Neow's");
                c.Equal("""{"act":1,"floor":1,"ascension":0}""", Text(s.State["run"]), $"{mode}: on floor 1");
                c.Equal(3, s.State["options"]!.AsArray().Count, $"{mode}: three relics on offer");
                c.Equal("option:0 option:1 option:2", Ids(s), $"{mode}: one action each");
                c.That(s.Actions.All(action => action.Label.StartsWith("Take ", StringComparison.Ordinal)), $"{mode}: labelled Take <relic> ({Labels(s)})");
                c.Equal(RoomKind.Event, run.Room, $"{mode}: the hooks were told an Event room");
                var world = run.State.FindPart<RunWorld>()!;
                c.Equal(1, world.Rooms[0].EventsVisited, $"{mode}: Neow's room moved the act's event counter");
                c.Equal("(3,0)", string.Join(" ", run.State.FindPart<MapState>()!.Visited), $"{mode}: standing on the start point of the saved map");
                c.Equal((mode == RunMode.Strict, true), (run.State.FindPart<LeftOutLog>()?.Strict ?? false, ((ActDirector)run.Director).Mode == mode), $"{mode}: the strict switch is set for a strict run alone");

                var log = FloorLog.Of(run);
                c.Equal(1, log.Count, $"{mode}: the floor log has floor 1");
                c.Equal("floor  1  ancient (3,0)  Event Neow  HP 80/80 > 80/80  gold 99 > 99  deck 10 > 10  (not left)" + (log[0].DrawnAgain.Count > 0 ? $"  drawn again past {string.Join(", ", log[0].DrawnAgain)}" : ""),
                    log[0].ToString(), $"{mode}: its line");
                if (gamesOfferBuilt)
                {
                    c.Sequence(look.Neow.Select(relic => Content.Registry.NewRelic(relic).Name), s.State["options"]!.AsArray().Select(option => option!["title"]!.GetValue<string>()),
                        $"{mode}: the game's own offer for the seed, in its order");
                    c.Equal(0, Logged(run).Count, $"{mode}: nothing was passed over");
                }
                else
                {
                    c.That(Logged(run).Count > 0 && Logged(run).All(entry => entry is { Floor: 1, Kind: "relic" } && !RunRelics.IsBuilt(entry.ClassName)),
                        $"{mode}: the offer was drawn again past relics that are not built, and the log has them on floor 1");
                }
            }
        });
    }

    // ---------- the real run through the director ----------

    /// <summary>
    /// The real run on seed 1BUSLN0FY5 (the class comment of <see cref="RealRunChecks"/> has it
    /// floor by floor) through the act's director, as far as the registry lets the arena follow
    /// it. Returns the run and how many floors of the record it followed.
    /// </summary>
    private static (Arena.Run Run, int Followed) FollowTheRecord(Check c, RunMode mode, string eventOption)
    {
        var run = ActRun.Start(PlayedSeed, mode);
        string[] offer = ["PreciseScissors", "ArcaneScroll", "LeafyPoultice"];
        if (!offer.All(RunRelics.IsBuilt))
        {
            c.That(run.IsOver ? run.Result == RunResult.Unrepresentable : Logged(run).Count > 0,
                $"{mode}: Neow's offer of the record ({string.Join(", ", offer)}) is not all built, so the run is not the record's from floor 1 ({(run.IsOver ? run.ResultReason : "drawn again")})");
            return (run, 0);
        }

        // Floor 1: Neow. The record's pick was Arcane Scroll, which gave Barricade.
        Take(run, "Take Arcane Scroll");
        c.Equal("option:0=Leave", Labels(run.Snapshot()), $"{mode}: after the pick Neow's room has its way out and nothing else");
        Take(run, "Leave");
        c.Equal("Barricade", run.State.Deck[^1].Def.ClassName, $"{mode}: Arcane Scroll's card is Barricade, the record's");
        Take(run, "Go to Monster at (5,1)");

        // Floor 2: Shrinker Beetle; 11 gold, no potion, Colossus taken.
        PlayFight(run);
        if (run.IsOver) return (run, 1);
        Take(run, "Take 11 gold");
        Take(run, "Open card reward (3 cards)");
        Take(run, "Take Colossus");
        Take(run, "Proceed");
        Take(run, "Go to Monster at (6,2)");

        // Floor 3: Fuzzy Wurm Crawler; 10 gold, a Vulnerable Potion, Fight Me! taken.
        PlayFight(run);
        if (run.IsOver) return (run, 2);
        Take(run, "Take 10 gold");
        Take(run, "Take potion Vulnerable Potion");
        Take(run, "Open card reward (3 cards)");
        Take(run, "Take Fight Me!");
        Take(run, "Proceed");
        Take(run, "Go to Unknown (?) at (6,3)");

        // Floor 4: the Unknown point the game rolled to be an event, This or That? (the game's English titles: the event is
        // "This or That?", its option PLAIN is "This" and its option ORNATE, which the record took, is "That").
        if (run.IsOver) return (run, 3);
        var s = run.Snapshot();
        if (!EventList.IsBuilt("ThisOrThat"))
        {
            c.That(s.Screen != "event" || s.State["event"]!.GetValue<string>() != "This or That?",
                $"{mode}: This or That is not built, so floor 4 is not the record's (it is {s.Screen}{(s.Screen == "event" ? " " + s.State["event"] : "")})");
            return (run, 3);
        }
        c.Equal("event This or That?", $"{s.Screen} {s.State["event"]}", $"{mode}: floor 4 is the event of the record");
        Take(run, eventOption);
        return (run, 4);
    }

    private static void TheRealRun(Check c)
    {
        c.Group("the real run on seed 1BUSLN0FY5 through the act's director: the floor log is the game's record, floor by floor", () =>
        {
            var (run, followed) = FollowTheRecord(c, RunMode.DrawAgain, "Choose That");
            if (followed < 4 || run.IsOver)
            {
                c.That(followed < 4 || run.Result == RunResult.Unrepresentable, $"the registry lets the arena follow the record for {followed} floors ({(run.IsOver ? ActRun.Outcome(run) : "going on")})");
                c.Equal(run.Dump(), Arena.Run.Replay(ActRun.Setup(PlayedSeed), ActRun.Director(), run.Actions).Dump(), "what was played replays from its action list");
                return;
            }

            // Floor 4, after That (ORNATE): a relic from the front of the player's Uncommon list and the curse Clumsy.
            c.Equal("Clumsy", run.State.Deck[^1].Def.ClassName, "That added the curse Clumsy");
            Take(run, "Leave");
            Take(run, "Go to Shop at (6,4)");

            // Floor 5: the shop; the record's purchase was Tear Asunder, on sale for 77 of the 120 gold.
            Take(run, "Open the shop inventory to buy");
            Take(run, "Buy Tear Asunder for 77 gold (on sale)");
            Take(run, "Close the shop inventory");
            Take(run, "Leave the shop");
            var onward = run.Snapshot().Actions.First(action => action.Label.StartsWith("Go to Monster at ", StringComparison.Ordinal));
            Do(run, onward.Id);

            // Floor 6: the third hallway fight, on. The log, floor by floor, against the record.
            var log = FloorLog.Of(run);
            c.Equal(6, log.Count, "six floors, as the record has them");
            c.Sequence(["ancient (3,0) Event Neow", "monster (5,1) Monster ShrinkerBeetleWeak", "monster (6,2) Monster FuzzyWurmCrawlerWeak", "unknown (6,3) Event ThisOrThat", "shop (6,4) Shop ", "monster " + log[5].At + " Monster SlimesWeak"],
                log.Select(floor => $"{floor.Point} {floor.At} {floor.Room} {floor.What}"),
                "the map points of the save, the rooms they held and what was in them (the record's map_point_type, room_type and model_id)");
            c.Sequence([1, 2, 3, 4, 5, 6], log.Select(floor => floor.Floor), "floors 1 to 6");
            c.Sequence<string?>(["Neow", "Shrinker Beetle", "Fuzzy Wurm Crawler", "This or That?", null, null], log.Select(floor => floor.Title), "the event's title, or the monsters of a fight that is over (the record's monster_ids)");
            c.Sequence([99, 110, 120, 120, 43, 43], log.Select(floor => floor.GoldOut), "gold after each floor: the record's current_gold");
            c.Sequence([99, 99, 110, 120, 120, 43], log.Select(floor => floor.GoldIn), "and on entering each");
            c.Sequence([10, 11, 12, 13, 14, 15], log.Select(floor => floor.DeckIn), "the deck on entering: ten cards, then Barricade, Colossus, Fight Me!, Clumsy, Tear Asunder");
            c.Sequence([11, 12, 13, 14, 15, 15], log.Select(floor => floor.DeckOut), "and on leaving");
            c.Sequence([true, true, true, true, true, false], log.Select(floor => floor.Finished), "five rooms were left; the sixth is the one the player is in");
            c.Equal((80, 80, 80, 80), (log[0].HpIn, log[0].MaxHpIn, log[0].HpOut, log[0].MaxHpOut), "Neow's room: 80 of 80 HP in and out (the record's hp_healed is the heal from 0)");
            for (var i = 1; i < log.Count; i++)
                c.Equal((log[i - 1].HpOut, log[i - 1].MaxHpOut), (log[i].HpIn, log[i].MaxHpIn), $"floor {log[i].Floor} is entered with the HP floor {log[i - 1].Floor} was left with");
            c.Equal((log[3].HpIn, log[4].HpIn), (log[3].HpOut, log[4].HpOut), "the event's That and the shop cost no HP");
            c.Sequence([0, 1, 1, 0, 0, 0], log.Select(floor => floor.Fights.Count), "one fight on floors 2 and 3; the fight of floor 6 is not over");
            c.That(log[1].Fights[0] is { Encounter: "ShrinkerBeetleWeak", Room: "Monster", Enemies: "Shrinker Beetle", Won: true, TimedOut: false, HpBefore: 80 } first
                   && first.HpAfter == log[1].HpOut && first.Turns == log[1].Turns && first.Turns >= 1,
                "the first fight's line: the encounter, its monster, won, entered with 80 HP and left with the HP the floor was left with");
            c.Equal(run.Hp, log[5].HpOut, "the floor that is not left shows the HP as it stands in its fight");

            // The choices, by the floor they were taken on, with the labels the snapshots offered them under.
            string[][] expected =
            [
                ["event Take Arcane Scroll", "event Leave", "map Go to Monster at (5,1)"],
                ["rewards Take 11 gold", "rewards Open card reward (3 cards)", "card_reward Take Colossus", "rewards Proceed", "map Go to Monster at (6,2)"],
                ["rewards Take 10 gold", "rewards Take potion Vulnerable Potion", "rewards Open card reward (3 cards)", "card_reward Take Fight Me!", "rewards Proceed", "map Go to Unknown (?) at (6,3)"],
                ["event Choose That", "event Leave", "map Go to Shop at (6,4)"],
                ["shop Open the shop inventory to buy", "shop Buy Tear Asunder for 77 gold (on sale)", "shop Close the shop inventory", "shop Leave the shop", "map " + onward.Label],
            ];
            for (var i = 0; i < expected.Length; i++)
                c.Sequence(expected[i], log[i].Choices.Select(choice => $"{choice.Screen} {choice.Label}"), $"floor {i + 1}: every choice with its screen and label, in order");
            c.Sequence(["option:1", "option:0", "map:2"], log[0].Choices.Select(choice => choice.Id), "floor 1: the ids (Arcane Scroll is Neow's second option, the record's pick)");
            c.Sequence(["open", "buy:card0", "close", "proceed", onward.Id], log[4].Choices.Select(choice => choice.Id), "floor 5: the ids");
            c.That(log[5].Choices.All(choice => choice.Screen == "combat" && RunPlayers.IsPotionAction(choice.Id)), "floor 6 holds nothing but potions used in its fight, if any");
            c.Equal(run.Choices.Count, log.Sum(floor => floor.Choices.Count), "every choice of the run is on a floor of the log");
            c.That(run.Choices.Select(choice => choice.Action).SequenceEqual(run.Choices.Select(choice => choice.Action).Order()) && run.Choices.All(choice => run.Actions[choice.Action] == choice.Id),
                "and names its place in the run's action list");

            // What was drawn again, on the floor it was drawn: the relic of floor 4 (Stone Cracker in the game) and the shop's shelf.
            string[] uncommonFront = ["StoneCracker", "ParryingShield", "OrnamentalFan", "Kusarigama", "PenNib", "Pantograph", "SelfFormingClay", "Nunchaku", "MiniatureCannon", "MercuryHourglass"];
            var passedOver = uncommonFront.TakeWhile(relic => !RunRelics.IsBuilt(relic)).Select(relic => "relic:" + relic).ToList();
            c.Sequence(passedOver, log[3].DrawnAgain, "floor 4: the relics before the first built one of the saved Uncommon list, in its order (none once Stone Cracker is built)");
            c.Equal(0, log[0].DrawnAgain.Count + log[1].DrawnAgain.Count + log[2].DrawnAgain.Count, "floors 1 to 3 are the game's own");
            c.Equal(passedOver.Count > 0 ? 4 : log[4].DrawnAgain.Count > 0 ? 5 : null, run.State.FindPart<LeftOutLog>()?.FirstFloor, "so the run stops being the game's own on floor 4 (on floor 5 once Stone Cracker is built, never once the shelf is too)");
            c.That(log[4].DrawnAgain.All(entry => entry.StartsWith("card:", StringComparison.Ordinal) || entry.StartsWith("relic:", StringComparison.Ordinal) || entry.StartsWith("potion:", StringComparison.Ordinal)),
                $"floor 5: what the shelf passed over is cards, relics and potions ({string.Join(", ", log[4].DrawnAgain)})");
            if (!CardPools.IsBuilt("Discovery")) c.Equal("card:Discovery", log[4].DrawnAgain.FirstOrDefault(), "floor 5: first of all the game's Uncommon colorless card, Discovery");

            // The yardstick of plan 5.6: the fight that is on, played from the same state on copies of the run.
            var yard = run.Clone();
            var outcome = RunPlayers.PlayFight(yard, Greedy);
            c.That(outcome is { Encounter: "SlimesWeak", Floor: 6, Enemies: "Twig Slime (S) + Twig Slime (M) + Leaf Slime (S)" } && outcome.HpBefore == log[5].HpIn,
                $"a copy plays the fight of floor 6 to its end: the third hallway fight with the line-up of the game's record, entered with the floor's HP ({outcome})");
            c.Equal(outcome, RunPlayers.PlayFight(run.Clone(), Greedy), "a second copy played by the same player ends the same way");
            c.That(run.Fight is not null && run.State.TotalFloor == 6 && FloorLog.Of(run)[5].Fights.Count == 0, "and the run itself still stands in the fight");
            c.Equal(null, RunPlayers.PlayFight(yard, Greedy), "with no fight on there is nothing to play");

            // The line as text and as JSON.
            c.Equal("floor  2  monster (5,1)  Monster ShrinkerBeetleWeak [Shrinker Beetle, " + log[1].Turns + (log[1].Turns == 1 ? " turn" : " turns") + "]  HP 80/80 > " + log[1].HpOut + "/80  gold 99 > 110  deck 11 > 12" +
                    "  | reward:0 Take 11 gold; reward:0 Open card reward (3 cards); " + log[1].Choices[2].Id + " Take Colossus; proceed Proceed; map:0 Go to Monster at (6,2)",
                log[1].ToString(), "floor 2 as one line of text");
            var json = FloorLog.ToJson(run);
            c.Equal("floor,at,point,room,what,title,hp_in,max_hp_in,gold_in,deck_in,finished,hp_out,max_hp_out,gold_out,deck_out,fights,choices", Keys(json[1]), "floor 2 as JSON: the keys in order");
            c.Equal("floor,at,point,room,hp_in,max_hp_in,gold_in,deck_in,finished,hp_out,max_hp_out,gold_out,deck_out,choices" + (log[4].DrawnAgain.Count > 0 ? ",drawn_again" : ""), Keys(json[4]),
                "the shop's floor: no encounter, no fights");
            c.Equal("""{"screen":"rewards","id":"reward:0","label":"Take 11 gold"}""", Text(json[1]!["choices"]![0]), "a choice as JSON");
            c.Equal("encounter,room,enemies,turns,hp_before,hp_after,damage_taken,won,timed_out", Keys(json[1]!["fights"]![0]), "a fight as JSON");
            c.Equal(FloorLog.Describe(run), string.Join("\n", log), "Describe is the lines, one a floor");

            // The result of the run as it stands, and the run again from its save and as a copy.
            var result = ActResult.Of(run, "the record");
            c.Equal(("unfinished", 6, 43, 15, 2, (int?)null), (result.Result, result.Floor, result.Gold, result.DeckSize, result.Fights, result.MetFloor), "the run as it stands: on floor 6, 43 gold, 15 cards, two fights won");
            var replay = ActRun.Replay(PlayedSeed, RunMode.DrawAgain, run.Actions);
            c.Equal(run.Dump(), replay.Dump(), "the whole run replays from its action list");

            // The run as it stood when a choice was made: the first actions of the save, up to the choice's place in it.
            var pick = run.Choices.Single(choice => choice.Label == "Take Colossus");
            var then = ActRun.Replay(PlayedSeed, RunMode.DrawAgain, run.Actions.Take(pick.Action));
            var offered = then.Snapshot();
            c.Equal(("card_reward", 2, 110), (offered.Screen, then.State.TotalFloor, then.Gold), "replayed up to the pick of Colossus the run stands at that card reward: floor 2, the 11 gold taken");
            c.Equal(pick.Label, offered.Actions.Single(action => action.Id == pick.Id).Label, "and offers the choice under the label the log has");
            c.Sequence<string>(["Colossus", "Havoc", "Unrelenting"], offered.State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal), "with the three cards of the game's record");
            c.Equal(Text(json), Text(FloorLog.ToJson(replay)), "with the same floor log");
            c.Equal(Lines(run.Choices), Lines(replay.Choices), "and the same choices");
            var copy = run.Clone();
            c.Equal((run.Dump(), Text(json), Lines(run.Choices)), (copy.Dump(), Text(FloorLog.ToJson(copy)), Lines(copy.Choices)), "a copy has the same state, log and choices");
        });
    }

    private static void TheRealRunStrict(Check c)
    {
        c.Group("the same run under the strict switch: the game's own run until the first thing that is not built, then unrepresentable as it stood", () =>
        {
            // The record's option, That (ORNATE): the game gives Stone Cracker, the front of the player's Uncommon list.
            var (run, followed) = FollowTheRecord(c, RunMode.Strict, "Choose That");
            if (followed == 4)
            {
                if (RunRelics.IsBuilt("StoneCracker"))
                {
                    c.That(!run.IsOver && run.State.HasRelic("StoneCracker"), "Stone Cracker is built: the run has the game's relic and goes on");
                }
                else
                {
                    c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "Stone Cracker is not built: the run ends as unrepresentable, by itself");
                    c.That(run.ResultReason!.Contains("StoneCracker", StringComparison.Ordinal), $"the reason names the relic ({run.ResultReason})");
                    c.Equal((4, 13, false), (run.State.TotalFloor, run.State.Deck.Count, run.State.HasRelic("StoneCracker")), "standing on floor 4 as before the option: thirteen cards, no relic, no curse");
                    var log = FloorLog.Of(run);
                    c.Equal((4, false, "event Choose That"), (log.Count, log[^1].Finished, $"{log[^1].Choices[^1].Screen} {log[^1].Choices[^1].Label}"), "the log ends on floor 4, not left, with the option as its last choice");
                    var result = ActResult.Of(run, "the record");
                    c.Equal((RunResult.Unrepresentable, 4, (int?)4, "relic", (int?)null), (result.Result, result.Floor, result.MetFloor, ActRun.MissingKind(result.Reason), result.FirstRedrawFloor),
                        "the result: unrepresentable, met on floor 4, a relic; nothing was drawn again");
                    c.Equal(run.Dump(), ActRun.Replay(PlayedSeed, RunMode.Strict, run.Actions).Dump(), "which replays to the same end");
                }
            }

            // The other option, This (PLAIN), costs 6 HP for the event's gold and needs nothing that is not built. The shop on floor 5
            // then stocks its colorless cards before its relics and potions: Discovery and Calamity, which the seed alone fixes
            // (its relics follow rarities rolled on rewards, which stands one number lower without That's roll).
            (run, followed) = FollowTheRecord(c, RunMode.Strict, "Choose This");
            if (followed < 4 || run.IsOver)
            {
                c.That(followed < 4 || run.Result is RunResult.Unrepresentable or RunResult.Died, $"the registry lets a strict run follow the record for {followed} floors ({(run.IsOver ? ActRun.Outcome(run) : "going on")})");
                return;
            }
            c.Equal(0, Logged(run).Count, "up to the event nothing was drawn again: the run is the game's own");
            Take(run, "Leave");
            var before = run.Clone();
            Take(run, "Go to Shop at (6,4)");
            string[] shelf = ["Discovery", "Calamity"];
            var missing = shelf.FirstOrDefault(card => !CardPools.IsBuilt(card));
            if (missing is null)
            {
                c.That(!run.IsOver || (run.Result == RunResult.Unrepresentable && ActRun.MissingKind(run.ResultReason) is "relic" or "potion"),
                    "the shelf's colorless cards are built: the shop opens, unless one of its relics or potions is not");
                return;
            }
            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, $"the shelf needs {missing}, which is not built: the run ends as unrepresentable on entering the shop");
            c.That(run.ResultReason!.Contains(missing, StringComparison.Ordinal), $"the reason names it ({run.ResultReason})");
            c.Equal(before.State.Dump().ToJsonString(), run.State.Dump().ToJsonString(), "the state is the one before the path choice: on floor 4, in front of the map");
            c.Equal("map:1", run.Actions[^1], "the path choice stays in the save");
            var shopResult = ActResult.Of(run, "the record");
            c.Equal((4, (int?)5), (shopResult.Floor, shopResult.MetFloor), "the run stands on floor 4 and met the missing thing on floor 5");
            c.Equal("map Go to Shop at (6,4)", $"{run.Choices[^1].Screen} {run.Choices[^1].Label}", "the choice that led there is the last of the run's choices");
            c.Equal(4, FloorLog.Of(run).Count, "and the log has four floors, the last one left");
            c.Equal(run.Dump(), ActRun.Replay(PlayedSeed, RunMode.Strict, run.Actions).Dump(), "the save replays to the same end under the same mode");
            c.That(run.Dump() != ActRun.Replay(PlayedSeed, RunMode.DrawAgain, run.Actions).Dump(), "and to another state under another: the mode belongs to the save");
        });
    }

    // ---------- the choices a run keeps ----------

    private sealed class Keeper : RunDirector
    {
        public bool Keeps { get; init; } = true;

        public override bool KeepsChoices => Keeps;

        public override void Begin(Arena.Run run) => run.Push(new Desk(Script));

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => run.Push(new Desk(Script));

        private static readonly Dictionary<string, Action<Arena.Run, Desk>> Script = new(StringComparer.Ordinal)
        {
            ["gold"] = static (run, _) => run.GainGold(5),
            ["cut"] = static (run, _) => DeckEdits.Remove(run, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1) with { RequireManualConfirmation = true })),
            ["trip"] = static (run, _) =>
            {
                CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1) with { RequireManualConfirmation = true });
                throw new InvalidOperationException("tripped");
            },
            ["fight"] = static (run, _) =>
            {
                run.State.TotalFloor++;
                run.StartFight(new FightRequest { Monsters = static () => [new PlacedMonster(new CheckDummy { Hp = 300 })], ShuffleDeck = false });
            },
        };
    }

    /// <summary>One fight on floor 1 whose rewards cannot be made: what follows the won fight ends the run as unrepresentable.</summary>
    private sealed class NoRewards : RunDirector
    {
        public override bool KeepsChoices => true;

        public override void Begin(Arena.Run run)
        {
            run.State.TotalFloor = 1;
            FloorLog.Enter(run, new MapArrival(new MapCoord(2, 1), MapPointType.Monster, RoomKind.Monster), "Custom");
            run.StartFight(new FightRequest { Monsters = static () => [new PlacedMonster(new CheckDummy { Hp = 5 })], ShuffleDeck = false });
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight)
        {
            FloorLog.FightWon(run, fight);
            throw new RunUnrepresentableException("the rewards of this fight cannot be made");
        }
    }

    private static void TheChoices(Check c)
    {
        c.Group("a run that ends as unrepresentable when a won fight's rewards are made still shows that fight on its floor", () =>
        {
            // The starter deck in deck order: the first card is a Strike, 6 damage to a dummy of 5 HP.
            var run = new Arena.Run(new RunSetup { Seed = "NOREWARDS" }, new NoRewards());
            c.That(run.Fight is not null && FloorLog.Of(run) is [{ Floor: 1, Room: "Monster", What: "Custom", Finished: false, Fights.Count: 0 }], "the fight is on: the floor's line has no fight yet");
            Do(run, run.Snapshot().Actions.First(action => action.Id.StartsWith("play:", StringComparison.Ordinal)).Id);
            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable && run.ResultReason == "the rewards of this fight cannot be made", "the fight is won and what follows it ends the run as unrepresentable");
            var log = FloorLog.Of(run);
            c.That(log is [{ Floor: 1, Finished: false, Fights: [{ Won: true, Encounter: "Custom", Turns: 1, HpBefore: 80 }] }], $"the floor shows the won fight, which the undone step had written and taken back ({(log.Count == 1 ? log[0].ToString() : log.Count)})");
            var result = ActResult.Of(run, "nobody");
            c.Equal((RunResult.Unrepresentable, 1, 1, (int?)1), (result.Result, result.Floor, result.Fights, result.MetFloor), "and the result counts it: one fight won, what was missing met on floor 1");
            c.Equal(run.Dump(), Arena.Run.Replay(new RunSetup { Seed = "NOREWARDS" }, new NoRewards(), run.Actions).Dump(), "the run replays to the same end");
            c.Equal(Text(FloorLog.ToJson(run)), Text(FloorLog.ToJson(run.Clone())), "and a copy shows the same log");
        });

        c.Group("a run keeps its choices beside its action list: accepted actions outside a fight and potions inside one", () =>
        {
            var setup = new RunSetup { Seed = "CHOICES", Potions = ["FirePotion"] };
            var run = new Arena.Run(setup, new Keeper());
            c.Equal(0, run.Choices.Count, "none before the first action");

            Do(run, "gold");
            c.Equal("event gold gold", Lines(run.Choices), "an action of a screen: the screen, the id and the label it was offered under");
            c.Equal((0, 0), (run.Choices[0].Action, run.Choices[0].Floor), "with its place in the action list and the floor");

            c.That(!run.Execute("bogus").Ok && run.Choices.Count == 1 && run.Actions.Count == 1, "a refused action is no choice");

            // An action that asks for a card: the action, the pick and the confirm of its preview are three choices.
            Do(run, "cut");
            c.Equal(2, run.Choices.Count, "the action that asked is kept when the question comes up");
            Do(run, "card:0", "confirm");
            c.Equal("event gold gold | event cut cut | card_select card:0 Pick Strike to remove | card_select confirm Confirm: remove Strike", Lines(run.Choices),
                "the question's steps follow under the labels of the card choice");
            c.Sequence([0, 1, 2, 3], run.Choices.Select(choice => choice.Action), "each at its place in the save");
            c.Equal(9, run.State.Deck.Count, "the card is gone");

            // An action that fails after its question is taken back whole: the action and the steps of its question.
            Do(run, "trip", "card:0");
            c.Equal(6, run.Choices.Count, "while the question is open its steps are listed");
            var threw = false;
            try { run.Execute("confirm"); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "the action throws once it has its answer");
            c.Equal("gold cut card:0 confirm", string.Join(" ", run.Actions), "the save is as before the action");
            c.Equal(4, run.Choices.Count, "and so are the choices");
            c.Equal(9, run.State.Deck.Count, "nothing was removed");

            // A fight: its plays are not choices; a potion is.
            Do(run, "fight");
            var s = run.Snapshot();
            var play = s.Actions.First(action => action.Id.StartsWith("play:", StringComparison.Ordinal));
            var potion = s.Actions.First(action => action.Id.StartsWith("potion:", StringComparison.Ordinal));
            Do(run, play.Id);
            c.Equal(5, run.Choices.Count, "a card played in a fight is no choice of the list");
            Do(run, potion.Id);
            c.Equal($"combat {potion.Id} {potion.Label}", Lines(run.Choices.Skip(5)), "a potion used in a fight is, under the fight's label");
            c.That(potion.Label.Contains("Fire Potion", StringComparison.Ordinal), $"which names the potion ({potion.Label})");
            c.Equal((6, 1), (run.Choices[^1].Action, run.Choices[^1].Floor), "at its place in the save, on the fight's floor");
            Do(run, "end_turn");
            c.Equal(6, run.Choices.Count, "ending the turn is none");

            c.Equal(Lines(run.Choices), Lines(run.Clone().Choices), "a copy has the same choices");
            c.Equal(Lines(run.Choices), Lines(Arena.Run.Replay(setup, new Keeper(), run.Actions).Choices), "and a replay builds them again");

            // A director that does not ask keeps none, and reads no label.
            var plain = new Arena.Run(setup, new Keeper { Keeps = false });
            Do(plain, "gold", "cut", "card:0", "confirm", "fight");
            Do(plain, plain.Snapshot().Actions.First(action => action.Id.StartsWith("potion:", StringComparison.Ordinal)).Id);
            c.Equal((0, 6), (plain.Choices.Count, plain.Actions.Count), "a run of another director keeps no choices");
            c.That(!new FixedFights().KeepsChoices, "which is how every director is unless it says otherwise");
        });
    }

    // ---------- the share of real runs ----------

    private static ActResult Row(string result, int floor, int? metFloor = null, string? reason = null) =>
        new("SEED", "by hand", result, reason, floor, 1, 80, 0, 10, 1, 0, 0, 0, 0, null, false, null, null, metFloor, 0);

    private static void TheShareArithmetic(Check c)
    {
        c.Group("the share of real runs, on ten runs counted by hand", () =>
        {
            // Four seeds of another act; three runs stopped at Neow (floor 1); one met something unbuilt on floor 5; one died
            // on floor 4; one cleared the act.
            var rows = new List<ActResult>();
            for (var n = 0; n < 4; n++) rows.Add(Row(RunResult.Unrepresentable, 0, 0, "the first act of seed X is Underdocks; ..."));
            for (var n = 0; n < 3; n++) rows.Add(Row(RunResult.Unrepresentable, 0, 1, "Neow offers ..."));
            rows.Add(Row(RunResult.Unrepresentable, 4, 5, "a shop card on floor 5 landed on the card Discovery, which the arena leaves out"));
            rows.Add(Row(RunResult.Died, 4));
            rows.Add(Row(RunResult.Cleared, 17));

            var share = RealRunShare.Of(rows);
            c.Equal((10, 6, 1, 1, 2), (share.Started, share.Overgrowth, share.Cleared, share.Died, share.Finished), "ten started, six in Overgrowth, one cleared, one died");
            c.Equal(0.2, share.Plain, "the plain share: two of ten ended cleared or dead");
            // Floor 1: all six entered it, three met Neow's missing relic. Floors 2 and 3: the three left. Floor 4: the dead run
            // counts for the floors before the one it died on, so two. Floor 5: two, of which one met a missing card. Then one.
            c.Sequence([6, 3, 3, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1], share.AtRisk, "the runs each floor's figure rests on");
            c.Sequence([3, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], share.Met, "the runs that met something unbuilt on each floor");
            c.Sequence([0.5, 0.5, 0.5, 0.5, 0.25], share.Through.Take(5), "still the game's own after floors 1 to 5: a half after Neow, a quarter after floor 5");
            c.Equal(0.25, share.Through[16], "and through the boss's floor");
            c.Equal(0.15, share.WholeAct, "as whole acts: six tenths of the seeds times a quarter");
            c.That(share.ReachedTheEnd && share.Reached == 17, "one run was the game's own to the end");
            c.That(share.Describe("by hand").Contains("2 of 10 strict runs (20.0%)", StringComparison.Ordinal) && share.Describe("by hand").Contains("15.0% of all real runs (25.0% of the Overgrowth ones)", StringComparison.Ordinal),
                $"in words: {share.Describe("by hand")}");

            // Without the run that cleared the act nobody is left after floor 5, and the share of whole acts cannot be shown.
            rows.RemoveAt(rows.Count - 1);
            share = RealRunShare.Of(rows);
            c.Sequence([5, 2, 2, 1, 1, 0], share.AtRisk.Take(6), "at risk without the cleared run");
            c.Equal((4, false, 0.0), (share.Reached, share.ReachedTheEnd, share.WholeAct), "the last floor a run was the game's own after is floor 4");
            c.Equal(0.4, share.Through[0], "after Neow: two of five");
            c.That(share.Describe("by hand").Contains("too small for 9 runs to show: no run was still alive and the game's own after floor 5", StringComparison.Ordinal), $"in words: {share.Describe("by hand")}");

            // What kind of thing was missing, read off the reasons the parts give.
            (string Reason, string Kind)[] reasons =
            [
                ("the first act of seed F30CH975SF is Underdocks; the arena has built Overgrowth alone", "act"),
                ("the seed does not pass the screen (neow,events=1): the first act of seed X is Underdocks; the arena has built Overgrowth alone", "screen"),
                ("the seed does not pass the screen (neow,events=1): Neow offers A, B, C on seed X, and the arena leaves out B", "screen"),
                ("Neow offers LavaRock, ArcaneScroll, PrecariousShears on this seed, and the arena leaves out LavaRock (not built yet: chunk 22 (neow-rest) of the run-mode plan builds it)", "neow"),
                ("Neow's cursed relic can only be one the arena leaves out (CursedPearl, HeftyTablet)", "neow"),
                ("the event of an Event room on floor 6 landed on the event DenseVegetation, which the arena leaves out", "event"),
                ("the Event room on floor 4 holds LuminousChoir, which the arena leaves out (not built yet: chunk 24 of the run-mode plan (events-b))", "event"),
                ("a shop card on floor 4 landed on the card Shockwave, which the arena leaves out", "card"),
                ("a random potion on floor 4 landed on the potion ColorlessPotion, which the arena leaves out", "potion"),
                ("the chest on floor 10: the game gives the relic TinyMailbox here, which the arena does not hold (not built yet: chunk 25 (relics-rooms) of the run-mode plan builds it)", "relic"),
                ("a shop relic on floor 5: no Shop relic the arena holds is left in the bag, and the rarity was not rolled (the game gives Orrery)", "relic"),
                ("something else entirely", "other"),
            ];
            foreach (var (reason, kind) in reasons) c.Equal(kind, ActRun.MissingKind(reason), $"'{reason[..Math.Min(60, reason.Length)]}...'");
            c.Equal("other", ActRun.MissingKind(null), "no reason");
        });
    }

    // ---------- the scripted players ----------

    private sealed class Still : RunDirector
    {
        public override void Begin(Arena.Run run) => run.Push(new Desk(new Dictionary<string, Action<Arena.Run, Desk>>()));
        public override void AfterFightWon(Arena.Run run, FightOutcome fight) { }
    }

    private static GameSnapshot Shown(string screen, string stateJson, params string[] ids) => new()
    {
        Screen = screen, AwaitingDecision = true, State = (JsonObject)JsonNode.Parse(stateJson)!, Actions = ids.Select(id => new LegalAction(id, id)).ToList(),
    };

    private static void ThePlayers(Check c)
    {
        c.Group("the player with plain rules: the first path, every reward, the first card, rest below half HP, smith otherwise, buy nothing", () =>
        {
            var run = new Arena.Run(new RunSetup(), new Still());
            var plain = new PlainPlayer();
            string Choose(string screen, string state, params string[] ids) => plain.Choose(run, Shown(screen, state, ids));
            const string none = "{}";

            c.Equal("Plain", plain.Name, "Plain plays its fights with Lookahead");
            c.Equal("Plain:greedy", RunPlayers.ByName("plain:Greedy")!.Name, "another fight player is part of the name");
            c.Equal("map:0", Choose("map", none, "map:0", "map:1", "potion:0", "discard_potion:0"), "the first path");
            c.Equal("continue", Choose("game_over", none, "continue"), "game over: continue");

            // Rewards: every reward in turn, then on; a potion is thrown away only when the screen would hold the player.
            c.Equal("reward:0", Choose("rewards", none, "reward:0", "reward:1", "proceed", "discard_potion:0"), "the first reward");
            c.Equal("proceed", Choose("rewards", none, "proceed", "discard_potion:0"), "nothing left to take (or a potion the belt has no room for): on");
            c.Equal("discard_potion:1", Choose("rewards", none, "discard_potion:1", "discard_potion:2"), "rewards that may not be skipped and a full belt: room is made");
            c.Equal("card:0", Choose("card_reward", none, "card:0", "card:1", "card:2", "skip"), "the first card of a card reward");

            // A card choice: the first card that is not picked yet, and confirm as soon as it is offered.
            const string picked = """{"cards":[{"i":0,"name":"Strike","selected":true},{"i":1,"name":"Defend"},{"i":5,"name":"Bash"}]}""";
            c.Equal("card:0", Choose("card_select", """{"cards":[{"i":0,"name":"Strike"},{"i":1,"name":"Defend"}]}""", "card:0", "card:1", "cancel"), "the first card");
            c.Equal("card:1", Choose("card_select", picked, "card:0", "card:1", "card:5"), "with the first card picked, the second: the first one's id would take the pick back");
            c.Equal("confirm", Choose("card_select", picked, "card:0", "card:1", "confirm", "cancel"), "confirm once it is offered");
            c.Equal("skip", Choose("card_select", none, "skip"), "a choice with nothing but skip");

            // The rest site: Rest below half HP, Smith otherwise; Rest when Smith is not on offer; on once an option was used.
            const string rest39 = """{"hp":39,"max_hp":80,"options":[{"n":0,"id":"HEAL","name":"Rest"},{"n":1,"id":"SMITH","name":"Smith"}]}""";
            const string rest40 = """{"hp":40,"max_hp":80,"options":[{"n":0,"id":"HEAL","name":"Rest"},{"n":1,"id":"SMITH","name":"Smith"}]}""";
            const string noSmith = """{"hp":80,"max_hp":80,"options":[{"n":0,"id":"HEAL","name":"Rest"},{"n":1,"id":"SMITH","name":"Smith","available":false}]}""";
            const string used = """{"hp":80,"max_hp":80,"options":[],"chosen":["Smith"]}""";
            c.Equal("option:0", Choose("rest", rest39, "option:0", "option:1"), "39 of 80 HP is below half: Rest");
            c.Equal("option:1", Choose("rest", rest40, "option:0", "option:1"), "40 of 80 is not: Smith");
            c.Equal("option:0", Choose("rest", noSmith, "option:0"), "Smith is not on offer (nothing to upgrade): Rest");
            c.Equal("proceed", Choose("rest", used, "proceed"), "an option was used: on");
            c.Equal("option:2", Choose("rest", """{"hp":80,"max_hp":80,"options":[{"n":2,"id":"DIG","name":"Dig"}]}""", "option:2"), "an option it has no rule for is taken when it is all there is");

            // The shop: nothing is bought.
            c.Equal("proceed", Choose("shop", none, "open", "proceed", "discard_potion:0"), "a shop is left as it is");
            c.Equal("close", Choose("shop", none, "buy:card0", "buy:remove", "close"), "an open inventory is closed");

            // The chest: opened, the relic taken, then on.
            c.Equal("open", Choose("treasure", none, "open"), "the chest is opened");
            c.Equal("relic:0", Choose("treasure", none, "relic:0"), "its relic taken");
            c.Equal("proceed", Choose("treasure", none, "proceed"), "and the room left");

            // An event: the first option that is not the way out.
            c.Equal("option:0", Choose("event", """{"options":[{"n":0,"title":"This"},{"n":1,"title":"That"}]}""", "option:0", "option:1"), "the first option of an event (Neow's first relic)");
            c.Equal("option:1", Choose("event", """{"options":[{"n":0,"title":"Pay","locked":true},{"n":1,"title":"Walk"}]}""", "option:1"), "a locked option is not on offer: the next one");
            c.Equal("option:0", Choose("event", """{"finished":true,"options":[{"n":0,"title":"Proceed","leave":true}]}""", "option:0"), "the way out when it is all there is");
            c.Equal("option:1", Choose("event", """{"options":[{"n":0,"title":"Leave","leave":true},{"n":1,"title":"Stay"}]}""", "option:0", "option:1"), "an option before the way out even when the way out is listed first");
        });

        c.Group("the random walker: any offered action, a potion one time in eight, the same walk every time", () =>
        {
            var walker = new RunWalker(3);
            c.Equal("Walker", walker.Name, "the walker plays its fights with Greedy");
            c.Equal("Walker:random", RunPlayers.ByName("Walker:random")!.Name, "or with whoever is named");
            c.That(RunPlayers.ByName("Stroller") is null && RunPlayers.ByName("Walker:nobody") is null, "a name nobody has gives no player");
            c.That(RunPlayers.Default.All(name => RunPlayers.ByName(name) is not null) && RunPlayers.Names.All(name => RunPlayers.ByName(name) is not null), "every listed name gives a player");

            var screen = Shown("map", "{}", "map:0", "map:1", "map:2", "potion:0", "discard_potion:0");
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            const int steps = 8000;
            for (var step = 0; step < steps; step++)
            {
                var id = walker.Pick(screen, step);
                counts[id] = counts.GetValueOrDefault(id) + 1;
            }
            c.That(counts.Keys.All(id => screen.Actions.Any(action => action.Id == id)) && counts.Count == 5, $"only offered actions, all of them ({string.Join(", ", counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value}"))})");
            // A potion one time in eight: 1,000 of 8,000, give or take four deviations of about 30. The rest evenly: about 2,333 each, deviation 41.
            var potions = counts.GetValueOrDefault("potion:0") + counts.GetValueOrDefault("discard_potion:0");
            c.That(potions is >= 880 and <= 1120, $"a potion action about one time in eight ({potions} of {steps})");
            foreach (var id in new[] { "map:0", "map:1", "map:2" })
                c.That(counts.GetValueOrDefault(id) is >= 2170 and <= 2500, $"{id} about a third of the rest ({counts.GetValueOrDefault(id)})");
            c.That(Enumerable.Range(0, 200).All(step => walker.Pick(screen, step) == new RunWalker(3).Pick(screen, step)), "the same seed, step and screen give the same answer");
            c.That(Enumerable.Range(0, 200).Any(step => walker.Pick(screen, step) != new RunWalker(4).Pick(screen, step)), "another seed walks another way");
            c.Equal("discard_potion:0", walker.Pick(Shown("rewards", "{}", "discard_potion:0"), 5), "with nothing but a potion action on offer, that one");
            c.Equal("continue", walker.Pick(Shown("game_over", "{}", "continue"), 5), "game over: continue");
        });

        c.Group("a card choice inside a fight is confirmed after twelve picks in a row, so that no player picks and takes back for ever", () =>
        {
            var choice = Shown("card_select", "{}", "card:0", "card:1", "confirm");
            var picks = Enumerable.Repeat("card:0", RunPlayers.MostPicksInARow).ToList();
            c.Equal("confirm", RunPlayers.Guard("card:0", choice, ["play:0>e0", .. picks]), "twelve picks in a row and a confirm on offer: confirm");
            c.Equal("card:0", RunPlayers.Guard("card:0", choice, ["play:0>e0", .. picks.Skip(1)]), "eleven: the fight player's own answer");
            c.Equal("card:0", RunPlayers.Guard("card:0", choice, [.. picks, "play:0>e0", "card:1"]), "picks before another action do not count");
            c.Equal("card:0", RunPlayers.Guard("card:0", Shown("card_select", "{}", "card:0", "card:1"), picks), "no confirm on offer: nothing to press in its place");
            c.Equal("select:2", RunPlayers.Guard("select:2", Shown("hand_select", "{}", "select:2", "confirm"), picks.Take(3).ToList()), "a hand selection after three picks goes on");
            c.Equal("confirm", RunPlayers.Guard("select:2", Shown("hand_select", "{}", "select:2", "confirm"), Enumerable.Repeat("select:2", 12).ToList()), "and is confirmed after twelve");
            c.Equal("play:1", RunPlayers.Guard("play:1", Shown("combat", "{}", "play:1", "end_turn"), picks), "a play is left alone");
        });
    }

    // ---------- whole acts ----------

    /// <summary>The titles of everything the registry holds, by kind: what a snapshot outside a fight may show.</summary>
    private sealed class KnownNames
    {
        public readonly HashSet<string> Cards = new(StringComparer.Ordinal);
        public readonly HashSet<string> Relics = new(StringComparer.Ordinal);
        public readonly HashSet<string> Potions = new(StringComparer.Ordinal);
        public readonly HashSet<string> Events = new(StringComparer.Ordinal);

        public KnownNames()
        {
            var registry = Content.Registry;
            var state = new RunState(new RunSetup());
            foreach (var def in registry.Cards.Values)
            {
                Cards.Add(state.NewCard(def).Title);
                if (def.MaxUpgradeLevel > 0) Cards.Add(state.NewCard(def, upgraded: true).Title);
            }
            foreach (var name in registry.RelicNames) Relics.Add(registry.NewRelic(name).Name);
            foreach (var potion in registry.Potions.Values) Potions.Add(potion.Name);
            foreach (var name in registry.EventNames) Events.Add(registry.NewEvent(name).Title);
        }

        /// <summary>Every name a snapshot outside a fight shows that the registry does not hold, as "kind name".</summary>
        public List<string> Strangers(GameSnapshot s)
        {
            var found = new List<string>();
            void Card(JsonNode? node)
            {
                if (node is JsonObject o && o["name"] is JsonValue name && !Cards.Contains(name.GetValue<string>())) found.Add($"card {name}");
            }
            void Relic(JsonNode? node)
            {
                var name = node is JsonObject o ? o["name"]?.GetValue<string>() : node?.GetValue<string>();
                if (name is not null && !Relics.Contains(name)) found.Add($"relic {name}");
            }
            void Potion(JsonNode? node)
            {
                var name = node is JsonObject o ? o["name"]?.GetValue<string>() : node is JsonValue v ? v.GetValue<string>() : null;
                if (name is not null && !Potions.Contains(name)) found.Add($"potion {name}");
            }
            void Each(JsonNode? list, Action<JsonNode?> check)
            {
                if (list is JsonArray array)
                    foreach (var item in array) check(item);
            }

            var state = s.State;
            if (state["player"] is JsonObject player)
            {
                Each(player["relics"], Relic);
                Each(player["potions"], Potion);
                if (player["deck"] is JsonObject deck)
                    foreach (var (name, _) in deck)
                        if (!Cards.Contains(name)) found.Add($"card {name}");
            }
            Each(state["potions"], Potion);
            Each(state["cards"], Card);
            Each(state["relics"], Relic);
            if (state["preview"] is JsonArray) Each(state["preview"], Card);
            if (state["rewards"] is JsonArray rewards)
            {
                foreach (var row in rewards.OfType<JsonObject>())
                {
                    switch (row["kind"]?.GetValue<string>())
                    {
                        case "potion": Potion(row); break;
                        case "relic": Relic(row); break;
                        case "special_card": Card(row); break;
                        case "card": Each(row["cards"], Card); break;
                    }
                }
            }
            if (state["stock"] is JsonObject stock)
            {
                Each(stock["cards"], Card);
                Each(stock["colorless"], Card);
                Each(stock["relics"], Relic);
                Each(stock["potions"], Potion);
            }
            if (s.Screen == "event")
            {
                if (state["event"] is JsonValue title && !Events.Contains(title.GetValue<string>())) found.Add($"event {title}");
                if (state["options"] is JsonArray options)
                {
                    foreach (var option in options.OfType<JsonObject>())
                    {
                        if (option["relic"] is not null) Relic(option["relic"]);
                        Each(option["cards"], Card);
                        // An Ancient's option is its relic under the relic's own name.
                        if (state["ancient"] is not null && option["leave"] is null) Relic(option["title"]);
                    }
                }
            }
            return found;
        }
    }

    /// <summary>What a batch of walked acts found.</summary>
    private sealed class Walked
    {
        public readonly ConcurrentBag<string> Problems = new();
        public readonly ConcurrentDictionary<string, int> Results = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, int> Screens = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<RoomKind, int> Rooms = new();
        public readonly ConcurrentDictionary<string, int> Unrepresentable = new(StringComparer.Ordinal);
        public int Runs, Floors, Actions, Shops, GamesOwn, Began, ScreenChecked, ShopColorlessChecked, FirstEventChecked;
    }

    private static readonly IReadOnlySet<string> AlwaysAllowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "AromaOfChaos", "JungleMazeAdventure", "SapphireSeed", "SunkenStatue", "TabletOfTruth", "Wellspring", "DenseVegetation", "SelfHelpBook", "ThisOrThat", "BrainLeech", "RoomFullOfCheese",
    };

    /// <summary>
    /// One act walked by a player with everything looked at on the way: no action is turned down,
    /// nothing unbuilt is shown, every shop is full on arrival, what was bought was paid for and is
    /// held; at the end the run replays from its action list to the same state, the same floor log
    /// and the same choices, and a copy taken on the way plays on to the same end.
    /// </summary>
    private static Arena.Run WalkOne(string seed, IRunPlayer player, RunMode mode, StreamMode streams, KnownNames shown, Walked walked)
    {
        var id = $"{seed} ({player.Name}, {mode}{(streams == StreamMode.Keyed ? ", keyed" : "")})";
        void Problem(string what) => walked.Problems.Add($"{id}: {what}");

        var run = ActRun.Start(seed, mode, streams);
        Interlocked.Increment(ref walked.Runs);
        if (!run.IsOver) Interlocked.Increment(ref walked.Began);
        var look = SeedLook.Of(seed);
        Arena.Run? halfway = null;
        var halfwayAt = 0;
        var shopFloor = 0;
        var fightsWon = 0;
        var firstEventSeen = false;
        var firstShopSeen = false;
        var firstSnapshot = true;

        for (var step = 0; !run.AtEnd; step++)
        {
            if (step >= RunPlayers.MostActions)
            {
                Problem($"the walk did not reach the end of the run (floor {run.State.TotalFloor}, {run.Snapshot().Screen})");
                break;
            }
            var s = run.Snapshot();
            walked.Screens.AddOrUpdate(s.Screen, 1, (_, n) => n + 1);
            if (s.Actions.Count == 0)
            {
                Problem($"{s.Screen} on floor {run.State.TotalFloor} offers no action ({s.Waiting})");
                break;
            }
            var floor = run.State.TotalFloor;

            if (run.Fight is null)
            {
                foreach (var stranger in shown.Strangers(s)) Problem($"{s.Screen} on floor {floor} shows the {stranger}, which the registry does not hold");

                // Neow's room first: on a seed whose offer is built it is the game's own.
                if (firstSnapshot && s.Screen == "event" && look.Neow.All(RunRelics.IsBuilt))
                {
                    Interlocked.Increment(ref walked.ScreenChecked);
                    var offered = s.State["options"]!.AsArray().Select(option => option!["title"]!.GetValue<string>()).ToList();
                    if (!offered.SequenceEqual(look.Neow.Select(relic => Content.Registry.NewRelic(relic).Name)))
                        Problem($"Neow offers {string.Join(", ", offered)}; the seed's own offer is {string.Join(", ", look.Neow)}, all built");
                }

                // The first Event room after Neow's: an event of the seed's walk, and the first of it when that one is built and always allowed.
                if (s.Screen == "event" && floor > 1 && !firstEventSeen)
                {
                    firstEventSeen = true;
                    var met = run.State.FindPart<EventsState>()?.Current?.ClassName;
                    if (met is null || !look.Events.Contains(met)) Problem($"the first Event room holds {met}, which is not on the seed's walk");
                    else if (look.Events.Count > 0 && EventList.IsBuilt(look.Events[0]) && AlwaysAllowed.Contains(look.Events[0]))
                    {
                        Interlocked.Increment(ref walked.FirstEventChecked);
                        if (met != look.Events[0]) Problem($"the first Event room holds {met}; the first event of the seed's walk, {look.Events[0]}, is built and always allowed");
                    }
                }

                // A shop on arrival: every place of the shelf is filled.
                if (s.Screen == "shop" && shopFloor != floor)
                {
                    shopFloor = floor;
                    Interlocked.Increment(ref walked.Shops);
                    var stock = s.State["stock"] as JsonObject;
                    int Count(string key) => (stock?[key] as JsonArray)?.Count ?? 0;
                    if (Count("cards") != 5 || Count("colorless") != 2 || Count("relics") != 3 || Count("potions") != 3 || stock?["removal"] is null)
                        Problem($"the shop on floor {floor} has an empty place: {Count("cards")} cards, {Count("colorless")} colorless cards, {Count("relics")} relics, {Count("potions")} potions, removal {(stock?["removal"] is null ? "missing" : "there")}");
                    if (stock is not null && stock.SelectMany(kv => kv.Value is JsonArray list ? list.OfType<JsonObject>() : kv.Value is JsonObject one ? [one] : []).Any(entry => entry["price"]!.GetValue<int>() <= 0))
                        Problem($"the shop on floor {floor} has something for nothing");

                    // The first shop of the run: its colorless cards are the ones the seed fixes.
                    if (!firstShopSeen && streams == StreamMode.Game)
                    {
                        firstShopSeen = true;
                        var colorless = (stock?["colorless"] as JsonArray)?.Select(card => card!["name"]!.GetValue<string>()).ToList() ?? [];
                        var games = look.ShopColorless;
                        var passed = Logged(run).Where(entry => entry.Floor == floor && entry.Kind == "card").Select(entry => entry.ClassName).ToList();
                        Interlocked.Increment(ref walked.ShopColorlessChecked);
                        if (!CardPools.IsBuilt(games[0]))
                        {
                            if (passed.FirstOrDefault() != games[0]) Problem($"the first shop passed over {string.Join(", ", passed)} first; the seed's Uncommon colorless card, {games[0]}, is not built");
                        }
                        else
                        {
                            if (colorless.ElementAtOrDefault(0) != Content.Registry.Card(games[0]).Name) Problem($"the first shop's Uncommon colorless card is {colorless.ElementAtOrDefault(0)}, not the seed's {games[0]}");
                            if (CardPools.IsBuilt(games[1]))
                            {
                                if (colorless.ElementAtOrDefault(1) != Content.Registry.Card(games[1]).Name) Problem($"the first shop's Rare colorless card is {colorless.ElementAtOrDefault(1)}, not the seed's {games[1]}");
                            }
                            else if (passed.FirstOrDefault() != games[1]) Problem($"the first shop passed over {string.Join(", ", passed)} first; the seed's Rare colorless card, {games[1]}, is not built");
                        }
                    }
                }
            }
            firstSnapshot = false;

            string choice;
            try
            {
                choice = player.Choose(run, s);
            }
            catch (Exception ex)
            {
                Problem($"{player.Name} threw on {s.Screen} on floor {floor}: {ex.GetType().Name}: {ex.Message}");
                break;
            }
            var (relicsBefore, cardsBefore, goldBefore, potionsBefore) = (run.Relics.Count, run.Deck.Count, run.Gold, run.Potions.Count(potion => potion is not null));
            var inFight = run.Fight is not null;
            ActionResult result;
            try
            {
                result = run.Execute(choice);
            }
            catch (Exception ex)
            {
                Problem($"'{choice}' on {s.Screen} on floor {floor} threw {ex.GetType().Name}: {ex.Message}");
                break;
            }
            if (!result.Ok)
            {
                Problem($"'{choice}' was refused on {s.Screen} on floor {floor}: {result.Error}");
                break;
            }

            // A fight that this action ended, as the run itself reports it.
            if (inFight && run.Fight is null && run.LastFight is { Won: true }) fightsWon++;

            // What was taken is held afterwards, and what was bought was paid for.
            bool Is(string prefix) => choice.StartsWith(prefix, StringComparison.Ordinal);
            if (run.Fight is null && !run.IsOver && run.Question is null)
            {
                if (Is("relic:") && s.Screen == "treasure" && run.Relics.Count != relicsBefore + 1) Problem($"after {choice} on floor {floor} the player holds {run.Relics.Count} relics, not {relicsBefore + 1}");
                if (Is("buy:relic") && run.Relics.Count != relicsBefore + 1) Problem($"after {choice} on floor {floor} the player holds {run.Relics.Count} relics, not {relicsBefore + 1}");
                if ((Is("buy:card") || Is("buy:colorless")) && run.Deck.Count != cardsBefore + 1) Problem($"after {choice} on floor {floor} the deck has {run.Deck.Count} cards, not {cardsBefore + 1}");
                if (Is("buy:potion") && run.Potions.Count(potion => potion is not null) != potionsBefore + 1) Problem($"after {choice} on floor {floor} the belt does not hold one potion more");
                if (Is("buy:") && choice != "buy:remove" && run.Gold >= goldBefore) Problem($"{choice} on floor {floor} cost nothing ({goldBefore} gold before, {run.Gold} after)");
                if (choice == "open" && s.Screen == "treasure" && run.Snapshot().State["relics"] is not null && run.Gold < goldBefore + TreasureRoom.MinGold)
                    Problem($"the chest of floor {floor} paid {run.Gold - goldBefore} gold");
            }
            if (run.Hp < 0 || run.Hp > run.MaxHp || run.Gold < 0) Problem($"{run.Hp}/{run.MaxHp} HP, {run.Gold} gold at floor {run.State.TotalFloor}");

            if (halfway is null && run.Fight is null && run.State.TotalFloor >= 6)
            {
                halfway = run.Clone();
                halfwayAt = run.Actions.Count;
            }
        }

        // ---------- the end ----------
        var how = run.Result ?? "unfinished";
        walked.Results.AddOrUpdate(how, 1, (_, n) => n + 1);
        Interlocked.Add(ref walked.Floors, run.State.TotalFloor);
        Interlocked.Add(ref walked.Actions, run.Actions.Count);
        var map = run.State.FindPart<MapState>();
        if (map is not null)
            foreach (var visit in map.History)
                foreach (var room in visit.Rooms) walked.Rooms.AddOrUpdate(room, 1, (_, n) => n + 1);

        var log = FloorLog.Of(run);
        if (run.Result == RunResult.Error) Problem($"ended in an error: {run.ResultReason}");
        if (run.Result == RunResult.Cleared && run.State.TotalFloor != RealRunShare.Floors) Problem($"cleared on floor {run.State.TotalFloor}, not on the boss's floor 17");
        if (run.Result == RunResult.Cleared && !(log.Count == 17 && log[^1] is { Room: "Boss", Finished: true } && log[^1].Fights is [{ Won: true }])) Problem("cleared without a won boss fight on a finished floor 17 in the log");
        if (run.Result == RunResult.Died && run.Hp != 0 && run.LastFight is not { TimedOut: true }) Problem($"died with {run.Hp} HP");
        if (map is not null && map.History.Count != run.State.TotalFloor) Problem($"{map.History.Count} points visited on floor {run.State.TotalFloor}");
        if (run.State.Relics.Select(relic => relic.ClassName).Distinct().Count() != run.State.Relics.Count) Problem($"a relic is held twice ({string.Join(", ", run.State.Relics.Select(relic => relic.ClassName))})");

        // The floor log: one line a floor, in order, each entered as the one before was left; every choice on a floor.
        if (log.Count != run.State.TotalFloor) Problem($"the floor log has {log.Count} floors on floor {run.State.TotalFloor}");
        for (var i = 0; i < log.Count; i++)
        {
            var line = log[i];
            if (line.Floor != i + 1) Problem($"the log's line {i} is floor {line.Floor}");
            if (i > 0)
            {
                // Between leaving a room and entering the next only a potion used on the map can change the player (its HP).
                var before = log[i - 1];
                var potionOnTheMap = before.Choices.Any(choice => choice.Screen == "map" && choice.Id.StartsWith("potion:", StringComparison.Ordinal));
                if ((before.GoldOut, before.DeckOut) != (line.GoldIn, line.DeckIn) || (!potionOnTheMap && (before.HpOut, before.MaxHpOut) != (line.HpIn, line.MaxHpIn)))
                    Problem($"floor {line.Floor} is entered with {line.HpIn}/{line.MaxHpIn} HP, {line.GoldIn} gold, {line.DeckIn} cards; floor {i} was left with {before.HpOut}/{before.MaxHpOut}, {before.GoldOut}, {before.DeckOut}{(potionOnTheMap ? " (a potion was used on the map)" : "")}");
            }
            if (i < log.Count - 1 && !line.Finished) Problem($"floor {line.Floor} is not the last and was not left");
            if (i < log.Count - 1 && !(line.Choices.Count > 0 && line.Choices[^1].Id.StartsWith("map:", StringComparison.Ordinal) && line.Choices[^1].Label.EndsWith($"at {log[i + 1].At}", StringComparison.Ordinal)))
                Problem($"floor {line.Floor} does not end with the path choice to {log[i + 1].At} ({(line.Choices.Count > 0 ? line.Choices[^1].Label : "no choice")})");
            if (line.Room is "Monster" or "Elite" or "Boss" && line.What is null) Problem($"floor {line.Floor} is a fight without an encounter");
            if (line.Room == "Event" && (line.What is null || !EventList.IsBuilt(line.What))) Problem($"floor {line.Floor} is an Event room with {line.What}, which is not built");
        }
        if (log.Sum(line => line.Choices.Count) != run.Choices.Count) Problem($"{run.Choices.Count} choices, {log.Sum(line => line.Choices.Count)} of them on the log's floors");

        // The k-th Monster room of the act holds the k-th entry of its hallway list, the k-th Elite room the k-th elite, the boss's
        // room the act's boss (plan 1.4); a fight is of its room's kind; a run that died in a fight has it as its last line's last fight.
        if (run.State.FindPart<RunWorld>() is { IsBegun: true } world && log.Count > 0)
        {
            var lists = world.Rooms[0];
            var monsters = log.Where(line => line.Room == "Monster").Select(line => line.What).ToList();
            var elites = log.Where(line => line.Room == "Elite").Select(line => line.What).ToList();
            if (!monsters.SequenceEqual(monsters.Select((_, k) => lists.NormalEncounters[k % lists.NormalEncounters.Count])))
                Problem($"the Monster rooms hold {string.Join(", ", monsters)}; the hallway list starts {string.Join(", ", lists.NormalEncounters.Take(monsters.Count))}");
            if (!elites.SequenceEqual(elites.Select((_, k) => lists.EliteEncounters[k % lists.EliteEncounters.Count])))
                Problem($"the Elite rooms hold {string.Join(", ", elites)}; the elite list starts {string.Join(", ", lists.EliteEncounters.Take(elites.Count))}");
            if (log.FirstOrDefault(line => line.Room == "Boss") is { } boss && (boss.What != lists.Boss || boss.Floor != RealRunShare.Floors)) Problem($"the boss's room is {boss.What} on floor {boss.Floor}; the act's boss is {lists.Boss}");
            foreach (var line in log.Where(line => line.Room is "Monster" or "Elite" or "Boss"))
                if (line.Fights.Any(fight => fight.Room != line.Room || fight.Encounter != line.What)) Problem($"floor {line.Floor} is a {line.Room} room with {line.What} and holds the fight {string.Join(", ", line.Fights.Select(fight => $"{fight.Encounter} ({fight.Room})"))}");
        }
        if (run.Result == RunResult.Died && run.LastFight is { Won: false } lost && !(log.Count > 0 && !log[^1].Finished && log[^1].Fights.Count > 0 && log[^1].Fights[^1] is { Won: false } last && last.Encounter == lost.Encounter && log[^1].HpOut == run.Hp))
            Problem($"the run died in {lost.Encounter} on floor {lost.Floor}, and the log's last line does not end with that fight, lost");
        if (log.SelectMany(line => line.Fights).Count(fight => !fight.Won) > (run.Result == RunResult.Died && run.LastFight is { Won: false } ? 1 : 0)) Problem("the log holds a lost fight that did not end the run");
        // The fights the walk saw won are the won fights of the log and of the result line, whatever ended the run.
        var summed = ActResult.Of(run, player.Name);
        if (log.Sum(line => line.Fights.Count(fight => fight.Won)) != fightsWon || summed.Fights != fightsWon) Problem($"{fightsWon} fights were won; the log holds {log.Sum(line => line.Fights.Count(fight => fight.Won))} and the result says {summed.Fights}");
        if (summed.Turns != log.Sum(line => line.Fights.Sum(fight => fight.Turns)) || (fightsWon > 0 && summed.Turns < fightsWon)) Problem($"the result counts {summed.Turns} turns for {fightsWon} won fights");
        if ((summed.Floor, summed.Hp, summed.Gold, summed.DeckSize, summed.Actions, summed.Result) != (run.State.TotalFloor, run.Hp, run.Gold, run.Deck.Count, run.Actions.Count, how)) Problem("the result line does not show the run as it ended");
        if (log.Count > 0 && !log[^1].Finished && (log[^1].HpOut, log[^1].GoldOut, log[^1].DeckOut) != (run.Hp, run.Gold, run.Deck.Count)) Problem("the last floor does not show the player as it stands");

        // What was drawn again is not built; a strict run draws nothing again; a run that drew nothing again is the game's own.
        foreach (var entry in Logged(run))
        {
            var built = entry.Kind switch
            {
                "relic" => RunRelics.IsBuilt(entry.ClassName),
                "card" => CardPools.IsBuilt(entry.ClassName),
                "potion" => Content.Registry.Has(ContentKind.Potion, entry.ClassName),
                "event" => EventList.IsBuilt(entry.ClassName),
                _ => false,
            };
            if (built) Problem($"the log says {entry.Kind} {entry.ClassName} was passed over on floor {entry.Floor}, but it is built");
        }
        foreach (var entry in Logged(run).Where(entry => entry.Kind == "event"))
            if (log.FirstOrDefault(line => line.Floor == entry.Floor) is not { Point: "unknown" }) Problem($"the event {entry.ClassName} was passed over on floor {entry.Floor}, which is no Unknown point");
        if (mode == RunMode.Strict && Logged(run).Count > 0) Problem("a strict run drew something again");
        if (run.Result is RunResult.Cleared or RunResult.Died && Logged(run).Count == 0) Interlocked.Increment(ref walked.GamesOwn);
        if (run.Result == RunResult.Unrepresentable)
        {
            var kind = ActRun.MissingKind(run.ResultReason);
            walked.Unrepresentable.AddOrUpdate(kind, 1, (_, n) => n + 1);
            // Outside strict mode a run ends this way only where no redraw can help (the reasons of the parts' house rules).
            // (Never "no event is allowed and unmet": the director's CanHold keeps an Unknown point from being an Event room then.)
            string[] noRedraw = ["is left in the bag", "Circlet", "can only land on", "can only be one the arena leaves out", "fewer than two", "holds no relic"];
            if (mode != RunMode.Strict && !noRedraw.Any(text => run.ResultReason!.Contains(text, StringComparison.Ordinal)))
                Problem($"ended as unrepresentable outside strict mode for a reason a redraw could have helped: {run.ResultReason}");
        }

        // The save is the run, and a copy taken on the way plays on to the same end.
        var director = ActRun.Director(mode, streams);
        Arena.Run replayed;
        try
        {
            replayed = Arena.Run.Replay(ActRun.Setup(seed), director, run.Actions);
        }
        catch (Exception ex)
        {
            Problem($"the action list does not replay: {ex.Message}");
            return run;
        }
        var (dump, again) = (run.Dump(), replayed.Dump());
        if (dump != again) Problem($"the replay of the action list ends in another state{Difference(dump, again)}");
        if (Text(FloorLog.ToJson(run)) != Text(FloorLog.ToJson(replayed))) Problem("the replay has another floor log");
        if (Lines(run.Choices) != Lines(replayed.Choices)) Problem("the replay has other choices");
        if (halfway is not null)
        {
            foreach (var action in run.Actions.Skip(halfwayAt)) halfway.Execute(action);
            if (halfway.Dump() != dump) Problem($"a copy taken at action {halfwayAt} and played on ends in another state");
            if (Text(FloorLog.ToJson(halfway)) != Text(FloorLog.ToJson(run))) Problem("a copy played on has another floor log");
        }
        return run;
    }

    private static void WholeActs(Check c)
    {
        c.Group("whole acts by the scripted players: no action turned down, nothing unbuilt shown, no empty shop place, every run replays and copies", () =>
        {
            // The acts of every self-check, and many more when the environment asks for a soak.
            var soak = Soak;
            var seeds = ActRun.Seeds(soak > 0 ? soak : 100, 20261011, RunMode.DrawAgain);
            var shown = new KnownNames();
            var walked = new Walked();
            var jobs = new List<(string Seed, IRunPlayer Player, RunMode Mode, StreamMode Streams)>();
            for (var i = 0; i < seeds.Count; i++)
            {
                // Both players on every seed; every fifth seed also with keyed streams, every tenth with the walker's floor.
                jobs.Add((seeds[i], RunPlayers.ByName("Walker", i)!, RunMode.DrawAgain, StreamMode.Game));
                jobs.Add((seeds[i], RunPlayers.ByName("Plain:greedy", i)!, RunMode.DrawAgain, StreamMode.Game));
                if (i % 5 == 0) jobs.Add((seeds[i], RunPlayers.ByName("Walker", i + 1)!, RunMode.DrawAgain, StreamMode.Keyed));
                if (i % 10 == 0) jobs.Add((seeds[i], RunPlayers.ByName("Walker:random", i)!, RunMode.DrawAgain, StreamMode.Game));
            }
            // Lookahead is slow (seconds an act): a few acts, so that the plain player as a baseline plays it is walked too.
            foreach (var seed in seeds.Take(soak > 0 ? Math.Max(3, soak / 20) : 3)) jobs.Add((seed, RunPlayers.ByName("Plain")!, RunMode.DrawAgain, StreamMode.Game));
            // Screened runs on seeds that pass the default screen, as a model will play them.
            var screened = ActRun.Seeds(soak > 0 ? Math.Max(20, soak / 10) : 20, 20261011, RunMode.Screened, out _, mostTried: 20_000);
            foreach (var seed in screened) jobs.Add((seed, RunPlayers.ByName("Plain:greedy")!, RunMode.Screened, StreamMode.Game));

            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Threads }, job => WalkOne(job.Seed, job.Player, job.Mode, job.Streams, shown, walked));

            var results = string.Join(", ", walked.Results.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key} {r.Value}"));
            c.Equal("", string.Join(" | ", walked.Problems.Order(StringComparer.Ordinal).Take(6)),
                $"{walked.Runs} acts: every action was accepted, nothing unbuilt was shown, every shop was full, every run replays and copies ({walked.Problems.Count} problems)");
            c.Equal(jobs.Count, walked.Runs, "every act was walked");
            c.Equal(walked.Runs, walked.Results.GetValueOrDefault(RunResult.Cleared) + walked.Results.GetValueOrDefault(RunResult.Died) + walked.Results.GetValueOrDefault(RunResult.Unrepresentable),
                $"every act ended cleared, with a death or as unrepresentable ({results})");

            if (!NeowCanOffer)
            {
                // Nothing of Neow's is built: no run can begin, and every one says so.
                c.Equal((0, walked.Runs), (walked.Began, walked.Unrepresentable.GetValueOrDefault("neow")), "the registry holds no offer for Neow: every run ends at its start as unrepresentable, because of Neow");
                Console.Out.WriteLine($"  run loop: {walked.Runs} acts started; none could begin, since none of Neow's relics is built ({results})");
                return;
            }

            c.Equal(screened.Count, jobs.Count(job => job.Mode == RunMode.Screened), "the screened runs were found");
            c.That(walked.Began == walked.Runs, $"every act began in Neow's room ({walked.Began} of {walked.Runs})");
            c.That(walked.Results.GetValueOrDefault(RunResult.Cleared) > 0, "some acts were cleared: the boss was beaten and its rewards' proceed ended the act");
            c.That(walked.Results.GetValueOrDefault(RunResult.Died) > 0, "some ended with a death");
            foreach (var screen in new[] { "map", "combat", "rewards", "card_reward", "card_select", "rest", "treasure", "shop", "event", "game_over" })
                c.That(walked.Screens.ContainsKey(screen), $"the walks met the screen {screen}");
            foreach (var room in new[] { RoomKind.Monster, RoomKind.Elite, RoomKind.Boss, RoomKind.Treasure, RoomKind.Shop, RoomKind.Event, RoomKind.RestSite })
                c.That(walked.Rooms.ContainsKey(room), $"and a {room} room");
            c.That(walked.Floors > walked.Runs * 8, $"the walks went deep: {walked.Floors} floors in {walked.Runs} acts");
            c.That(walked.Shops > 0 && walked.ShopColorlessChecked > 0, $"{walked.Shops} shops were met, {walked.ShopColorlessChecked} of them a run's first, whose colorless cards were held against the seed's");
            c.That(walked.ScreenChecked >= screened.Count, $"Neow's offer was held against the seed's own in {walked.ScreenChecked} runs (every screened run among them)");

            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  run loop: {walked.Runs} acts walked ({results}), {walked.Floors} floors, {walked.Actions} actions, {walked.Shops} shops; {walked.GamesOwn} runs ended with nothing drawn again; " +
                $"unrepresentable outside strict mode: {(walked.Unrepresentable.IsEmpty ? "none" : string.Join(", ", walked.Unrepresentable.OrderBy(u => u.Key, StringComparer.Ordinal).Select(u => $"{u.Key} {u.Value}")))}; {walked.Problems.Count} problems"));
        });
    }

    // ---------- strict runs and the share ----------

    private static void TheStrictShare(Check c)
    {
        c.Group("strict runs on seeds as they come: the share of real runs the build can represent, and a strict run is the draw-again run up to what is missing", () =>
        {
            var soak = Soak;
            var seeds = ActRun.Seeds(soak > 0 ? Math.Max(240, soak) : 240, 20261012, RunMode.Strict);
            string[] names = ["Walker", "Plain:greedy"];
            var strict = new ActResult[names.Length][];
            var problems = new ConcurrentBag<string>();
            var compared = 0;
            var shown = new KnownNames();
            var walked = new Walked();
            for (var p = 0; p < names.Length; p++) strict[p] = new ActResult[seeds.Count];

            Parallel.For(0, seeds.Count * names.Length, new ParallelOptions { MaxDegreeOfParallelism = Threads }, job =>
            {
                var (s, p) = (job / names.Length, job % names.Length);
                var seed = seeds[s];
                var player = RunPlayers.ByName(names[p], s)!;
                var run = WalkOne(seed, player, RunMode.Strict, StreamMode.Game, shown, walked);
                var result = ActResult.Of(run, player.Name);
                strict[p][s] = result;
                if (result.Finished && result.FirstRedrawFloor is not null) problems.Add($"{seed} ({player.Name}): a strict run finished after a redraw on floor {result.FirstRedrawFloor}");
                if (result.Unrepresentable && result.MetFloor is null) problems.Add($"{seed} ({player.Name}): an unrepresentable run without a floor");

                // The same seed and player with drawing again: the strict run's actions, then more (or the same end).
                if (result.Unrepresentable && result.MetFloor == 0) return;
                var game = ActRun.StartGame(seed, RunMode.DrawAgain, actionDelay: TimeSpan.Zero);
                var again = RunPlayers.Play(game, player);
                Interlocked.Increment(ref compared);
                if (again.Refused > 0 || again.Stalled) problems.Add($"{seed} ({player.Name}): the draw-again run had {again.Refused} actions refused{(again.Stalled ? " and hung" : "")}: {again.FirstRefusal}");
                var actions = game.Run.Actions;
                if (!run.Actions.SequenceEqual(actions.Take(run.Actions.Count)))
                    problems.Add($"{seed} ({player.Name}): the strict run's {run.Actions.Count} actions are not the start of the draw-again run's");
                if (result.Finished)
                {
                    if (actions.Count != run.Actions.Count || again.Result != result.Result || again.FirstRedrawFloor is not null || Text(FloorLog.ToJson(game.Run)) != Text(FloorLog.ToJson(run)))
                        problems.Add($"{seed} ({player.Name}): a strict run that finished ({result.Result}) is not the draw-again run ({again.Result}, first redraw on floor {again.FirstRedrawFloor})");
                }
                else if (!(again.FirstRedrawFloor == result.MetFloor || (again.Unrepresentable && again.MetFloor == result.MetFloor)))
                {
                    problems.Add($"{seed} ({player.Name}): the strict run met something unbuilt on floor {result.MetFloor}; the draw-again run first drew again on floor {again.FirstRedrawFloor} ({again.Result})");
                }
            });

            c.Equal("", string.Join(" | ", walked.Problems.Concat(problems).Order(StringComparer.Ordinal).Take(6)),
                $"{seeds.Count * names.Length} strict runs: no action turned down, nothing unbuilt shown, replays and copies; each is the draw-again run of its seed up to the action that met what is missing ({walked.Problems.Count + problems.Count} problems)");

            // The act roll is the game's coin: of 240 seeds between 89 and 151 start in Underdocks (four deviations either way of 120),
            // with a chance of all but 1 in 15,000.
            var underdocks = strict[0].Count(result => result.Unrepresentable && result.MetFloor == 0);
            var sigma = Math.Sqrt(seeds.Count) / 2;
            c.That(Math.Abs(underdocks - seeds.Count / 2.0) <= 4 * sigma, $"about half of the seeds start in Underdocks and end at once ({underdocks} of {seeds.Count})");
            c.That(strict[0].Zip(strict[1]).All(pair => (pair.First.MetFloor == 0) == (pair.Second.MetFloor == 0)), "the same seeds for both players");

            foreach (var (name, p) in names.Select((name, p) => (name, p)))
            {
                var share = RealRunShare.Of(strict[p]);
                c.Equal((seeds.Count, seeds.Count - underdocks), (share.Started, share.Overgrowth), $"{name}: the measurement counts every run and the Overgrowth seeds");
                c.Equal(strict[p].Count(result => result.Finished), share.Finished, $"{name}: and the runs that ended cleared or dead");
                c.That(share.AtRisk[0] == share.Overgrowth && share.Through.Zip(share.Through.Skip(1)).All(pair => pair.Second <= pair.First), $"{name}: every Overgrowth run enters floor 1, and the share never rises with the floor");
                c.Equal(strict[p].Count(result => result.Unrepresentable && result.MetFloor == 1), share.Met[0], $"{name}: the runs stopped in Neow's room are the ones met on floor 1");
                Console.Out.WriteLine("  run loop, strict: " + share.Describe(name).Replace("\n", "\n  ", StringComparison.Ordinal));
            }
            c.That(compared > 0, $"{compared} strict runs were held against their draw-again runs");
        });
    }

    // ---------- the command ----------

    /// <summary>The walker, except that once, on its fourth action, it names an action nobody offers.</summary>
    private sealed class Fumbler : IRunPlayer
    {
        private readonly RunWalker _walker = new(2);
        public string Name => "Fumbler";
        public string Choose(Arena.Run run, GameSnapshot snapshot) => run.Actions.Count == 3 ? "bogus:1" : _walker.Choose(run, snapshot);
    }

    private static void TheCommand(Check c)
    {
        c.Group("run-baseline reads its options", () =>
        {
            var defaults = RunBaselineCommand.Options.Parse([]);
            c.Equal((20, 1, RunMode.DrawAgain, StreamMode.Game, 1, false, false, 200_000), (defaults.Runs, defaults.Seed, defaults.Mode, defaults.Streams, defaults.Threads, defaults.Floors, defaults.Look, defaults.Tries),
                "the defaults: 20 runs from seed 1, drawing again, the game's streams, one thread, 200,000 seeds looked at");
            c.Sequence(["Walker", "Plain"], defaults.Players, "played by the walker and the plain player");
            c.Equal(SeedScreen.Default, defaults.Screen, "with the default screen");

            var asked = RunBaselineCommand.Options.Parse(["--runs", "7", "--seed", "3", "--mode", "screened", "--screen", "neow,events=2", "--players", "Plain:greedy,Walker:random", "--threads", "4", "--out", "somewhere", "--floors"]);
            c.Equal((7, 3, RunMode.Screened, 4, "somewhere", true), (asked.Runs, asked.Seed, asked.Mode, asked.Threads, asked.Out, asked.Floors), "every option");
            c.Equal(new SeedScreen(true, 2, 0, false), asked.Screen, "the screen");
            c.Sequence(["Plain:greedy", "Walker:random"], asked.Players, "the players as named");
            c.Sequence(["1BUSLN0FY5", "KMEHXZW0R5"], RunBaselineCommand.Options.Parse(["--seeds", "1busln0fy5, KMEHXZW0R5"]).Seeds, "seeds named, as the game writes them");
            c.Equal(StreamMode.Keyed, RunBaselineCommand.Options.Parse(["--streams", "keyed"]).Streams, "keyed streams");
            c.Equal(RunMode.Strict, RunBaselineCommand.Options.Parse(["--mode", "strict"]).Mode, "strict mode");

            (string[] Args, string Says)[] wrong =
            [
                (["--mode", "careful"], "--mode takes draw-again, screened or strict"),
                (["--streams", "keyed", "--mode", "strict"], "--streams keyed goes with --mode draw-again only"),
                (["--seeds", "1BUSLN0FY5", "--runs", "3"], "--seeds names the seeds itself"),
                (["--seeds", "not a seed!"], "is no run seed"),
                (["--players", "Stroller"], "There's no run player called 'Stroller'"),
                (["--players", "Walker,walker"], "--players names Walker twice"),
                (["--runs", "0"], "--runs takes a whole number from 1 to 100000"),
                (["--threads", "9"], "--threads takes a whole number from 1 to 8"),
                (["--runs"], "--runs needs a value"),
                (["--screen", "bogus", "--mode", "screened"], "--screen: 'bogus' is no test of the seed screen"),
                (["--screen", "full"], "--screen applies to --mode screened"),
                (["--scenarios", "3"], "run-baseline has no --scenarios option"),
                (["stray"], "'stray' has no option name in front of it"),
            ];
            foreach (var (args, says) in wrong)
            {
                string? message = null;
                try { RunBaselineCommand.Options.Parse(args); }
                catch (UsageException ex) { message = ex.Message; }
                c.That(message is not null && message.Contains(says, StringComparison.Ordinal), $"'{string.Join(" ", args)}' is turned down: {says} ({message})");
            }
            c.Equal(null, typeof(Policies).Assembly.GetTypes().FirstOrDefault(type => typeof(IRunPlayer).IsAssignableFrom(type) && typeof(IPolicy).IsAssignableFrom(type))?.Name,
                "no run player is a fight policy: the single-fight baseline finds its players by that interface and must not find these");
        });

        c.Group("run-baseline plays a batch, keeps the saves and carries a run on from its save", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "spiremind-run-loop-" + Guid.NewGuid().ToString("N"));
            try
            {
                var seeds = ActRun.Seeds(3, 5, RunMode.DrawAgain);
                string[] args = ["--seeds", string.Join(",", seeds), "--players", "Walker,Plain:greedy", "--out", folder, "--threads", "2"];
                var output = new StringWriter();
                var code = RunBaselineCommand.Run(args, output);
                var text = output.ToString();
                c.Equal(0, code, $"three acts by two players: every run came to its end with nothing refused ({text[..Math.Min(300, text.Length)]})");
                c.That(text.Contains("Arena run baseline: 3 runs of the first act (the seeds named), mode draw-again, the game's streams, played by Walker, Plain:greedy.", StringComparison.Ordinal), "the first line says what was played");
                c.That(seeds.All(seed => text.Contains(seed, StringComparison.Ordinal)), "every seed is listed");
                c.That(!text.Contains("REFUSED", StringComparison.Ordinal) && !text.Contains("HUNG", StringComparison.Ordinal) && !text.Contains("ERROR", StringComparison.Ordinal), "nothing that should never happen");

                var lines = File.ReadAllLines(Path.Combine(folder, "runs.jsonl"));
                c.Equal(6, lines.Length, "runs.jsonl has a line a run");
                var first = (JsonObject)JsonNode.Parse(lines[0])!;
                c.Equal("seed,player,result,reason,floor,hp,max_hp,gold,deck_size,relics,fights_won,turns,actions,refused,first_refusal,stalled,first_redraw_floor,died_to,met_floor,seconds,summary,floors,end,mode,streams",
                    Keys(first), "a result line's keys");
                c.Equal((seeds[0], "Walker", "draw-again", "game"), (first["seed"]!.GetValue<string>(), first["player"]!.GetValue<string>(), first["mode"]!.GetValue<string>(), first["streams"]!.GetValue<string>()), "the first line is the first seed's walker");

                string SaveOf(JsonObject line) => Path.Combine(folder, "actions", line["player"]!.GetValue<string>().Replace(':', '-'), line["seed"]!.GetValue<string>() + ".txt");
                var allSame = true;
                var resumed = 0;
                var parsed = lines.Select(line => (JsonObject)JsonNode.Parse(line)!).ToList();
                foreach (var line in parsed)
                {
                    var seed = line["seed"]!.GetValue<string>();
                    var path = SaveOf(line);
                    if (line["actions"]!.GetValue<int>() == 0)
                    {
                        // A run that was over before its first action (Neow has nothing built to offer) has nothing to save.
                        allSame &= !File.Exists(path);
                        continue;
                    }
                    var (savedSeed, actions) = RunGame.ReadActionLog(path);
                    var game = ActRun.Resume(seed, RunMode.DrawAgain, path, actionDelay: TimeSpan.Zero);
                    resumed++;
                    allSame &= savedSeed == seed && actions.Count == line["actions"]!.GetValue<int>() && game.AtEnd && game.Result == line["result"]!.GetValue<string>()
                               && Text(FloorLog.ToJson(game.Run)) == Text(line["floors"]) && game.Run.State.TotalFloor == line["floor"]!.GetValue<int>();
                }
                c.That(allSame, $"every save names its seed and holds the run: rebuilt from it, the run is over with the line's result, floor and floor log ({resumed} runs rebuilt)");
                c.Equal(NeowCanOffer ? 6 : 0, resumed, "every run that took an action has a save");

                // A run stopped in the middle: half of a save is cut off, and the same command plays it to the same end.
                var cut = parsed.FirstOrDefault(line => line["player"]!.GetValue<string>() == "Plain:greedy" && line["actions"]!.GetValue<int>() >= 4);
                if (cut is null)
                {
                    c.That(!NeowCanOffer, "no run took an action, since Neow has nothing built to offer: there is no save to carry on");
                    return;
                }
                var cutPath = SaveOf(cut);
                var all = File.ReadAllLines(cutPath);
                File.WriteAllLines(cutPath, all.Take(1 + (all.Length - 1) / 2));
                var second = new StringWriter();
                c.Equal(0, RunBaselineCommand.Run(args, second), "the same command again");
                c.That(second.ToString().Contains($"carried on from {cutPath} ({(all.Length - 1) / 2} actions)", StringComparison.Ordinal), "says which run it carried on, and from where");
                c.Sequence(all, File.ReadAllLines(cutPath), "the save is whole again, action for action");
                var again = File.ReadAllLines(Path.Combine(folder, "runs.jsonl")).Select(line => (JsonObject)JsonNode.Parse(line)!).ToList();
                c.That(again.Zip(lines.Select(line => (JsonObject)JsonNode.Parse(line)!)).All(pair => Text(pair.First["floors"]) == Text(pair.Second["floors"]) && Text(pair.First["result"]) == Text(pair.Second["result"])),
                    "and every run's line is what it was");

                // A save that does not fit (another seed's actions) is played again from the start.
                File.WriteAllLines(cutPath, [all[0], "map:9", "bogus"]);
                var third = new StringWriter();
                c.Equal(0, RunBaselineCommand.Run(args, third), "a save that does not fit does not stop the batch");
                c.That(third.ToString().Contains("did not fit and was played again from the start", StringComparison.Ordinal), "it says so");
                c.Sequence(all, File.ReadAllLines(cutPath), "and the run is played and saved again");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }

            // What should never happen is said and ends the command with 1: a player that names an action nobody offers.
            if (NeowCanOffer)
            {
                var seed = ActRun.Seeds(1, 5, RunMode.DrawAgain)[0];
                var fumbled = new StringWriter();
                var options = RunBaselineCommand.Options.Parse(["--seeds", seed]) with { Players = ["Fumbler"] };
                c.Equal(1, RunBaselineCommand.Run(options, fumbled, _ => new Fumbler()), "a batch in which an action was turned down ends with 1");
                c.That(fumbled.ToString().Contains($"REFUSED: Fumbler on {seed} had 1 actions turned down; the first: 'bogus:1' on ", StringComparison.Ordinal), $"and names the run, the action and the screen ({fumbled.ToString().Split('\n').FirstOrDefault(line => line.StartsWith("REFUSED", StringComparison.Ordinal))})");
                var played = RunPlayers.Play(seed, new Fumbler());
                c.Equal((1, false, true), (played.Refused, played.Stalled, played.Finished || played.Unrepresentable), "the run itself went on to its end after the one refusal: the first offered action was taken in its place");
            }

            // Strict mode prints the share; --look prints what the seeds fix and plays nothing.
            var strict = new StringWriter();
            c.Equal(0, RunBaselineCommand.Run(["--runs", "40", "--mode", "strict", "--players", "Walker", "--threads", "2"], strict), "forty strict runs");
            c.That(strict.ToString().Contains("mode strict", StringComparison.Ordinal) && strict.ToString().Contains("Share of real runs, Walker: ", StringComparison.Ordinal) && strict.ToString().Contains(" of 40 strict runs", StringComparison.Ordinal),
                "print the share of real runs");
            var looked = new StringWriter();
            c.Equal(0, RunBaselineCommand.Run(["--look", "--seeds", "1BUSLN0FY5,75F277YN2G"], looked), "a look at two real seeds");
            // A screen deeper than anything built finds no seed: the command says so and ends with 1.
            var nothing = new StringWriter();
            c.Equal(1, RunBaselineCommand.Run(["--mode", "screened", "--screen", "bags=31", "--runs", "2", "--tries", "200", "--players", "Walker"], nothing), "a screen no seed passes: nothing to play");
            c.That(nothing.ToString().Contains("Only 0 of the 2 seeds asked for were found among 200 (--tries)", StringComparison.Ordinal), $"it says how far it looked ({nothing.ToString().Trim()})");
            var lookText = looked.ToString();
            c.That(lookText.Contains("1BUSLN0FY5  Overgrowth", StringComparison.Ordinal) && lookText.Contains("75F277YN2G  Underdocks  does not pass: the first act of seed 75F277YN2G is Underdocks", StringComparison.Ordinal), "names each seed's act");
            c.That(lookText.Contains("PreciseScissors", StringComparison.Ordinal) && lookText.Contains("ArcaneScroll", StringComparison.Ordinal) && lookText.Contains("StoneCracker", StringComparison.Ordinal) && lookText.Contains("Discovery", StringComparison.Ordinal),
                "and what the Overgrowth seed fixes: Neow's offer, the bag ends, the first shop's colorless cards");
        });
    }
}
