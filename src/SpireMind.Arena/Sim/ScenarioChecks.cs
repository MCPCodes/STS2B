using System.Text.RegularExpressions;

namespace SpireMind.Arena;

/// <summary>
/// The scenario generator against run-generation.md: the game's rolls (encounter lists,
/// Unknown rooms, card rarities with the pity offset, potion drops) over many seeds against the
/// numbers the spec derives from the game's code; the scenarios themselves (tiers, floors,
/// decks, HP, belts) on the game's full card, potion and encounter lists; and what the
/// generator does with the registry as it is, whatever content that holds.
/// </summary>
public sealed class ScenarioChecks : IArenaChecks
{
    // The Ironclad's reward pool in a solo run with everything unlocked (run-generation.md 2.2):
    // 20 Common, 35 Uncommon, 25 Rare. Only names and rarities: the rules are tried on the whole
    // pool here whether or not the card files have every card.
    private static readonly string[] Commons =
    [
        "Anger", "Armaments", "BloodWall", "Bloodletting", "BodySlam", "Breakthrough", "Cinder", "Havoc", "Headbutt", "IronWave",
        "MoltenFist", "PerfectedStrike", "PommelStrike", "SetupStrike", "ShrugItOff", "SwordBoomerang", "Thunderclap", "Tremble", "TrueGrit", "TwinStrike",
    ];

    private static readonly string[] Uncommons =
    [
        "AshenStrike", "BattleTrance", "Bludgeon", "Bully", "BurningPact", "Colossus", "Dismantle", "Dominate", "DrumOfBattle", "EvilEye",
        "ExpectAFight", "FeelNoPain", "FightMe", "FlameBarrier", "ForgottenRitual", "Hemokinesis", "HowlFromBeyond", "InfernalBlade", "Inferno", "Inflame",
        "Juggling", "Pillage", "Rage", "Rampage", "Rupture", "SecondWind", "Spite", "Stampede", "Stomp", "StoneArmor",
        "Taunt", "Unrelenting", "Uppercut", "Vicious", "Whirlwind",
    ];

    private static readonly string[] Rares =
    [
        "Aggression", "Barricade", "Brand", "Cascade", "Conflagration", "CrimsonMantle", "Cruelty", "DarkEmbrace", "DemonForm", "Feed",
        "FiendFire", "Hellraiser", "Impervious", "Juggernaut", "Mangle", "NotYet", "Offering", "OneTwoPunch", "PactsEnd", "PrimalForce",
        "Pyre", "Stoke", "TearAsunder", "Thrash", "Unmovable",
    ];

    /// <summary>The game's whole lists: every reward card, every potion of the pool, every encounter of the act.</summary>
    private static Scenarios.Catalog Full(Func<string, bool>? card = null, Func<string, bool>? potion = null, Func<string, bool>? encounter = null) => new()
    {
        RewardCards = Commons.Select(n => (n, CardRarity.Common))
            .Concat(Uncommons.Select(n => (n, CardRarity.Uncommon)))
            .Concat(Rares.Select(n => (n, CardRarity.Rare)))
            .Where(c => card?.Invoke(c.n) ?? true)
            .OrderBy(c => c.n, StringComparer.Ordinal)
            .ToList(),
        PotionNames = Scenarios.PotionPool.Select(p => p.ClassName).Where(p => potion?.Invoke(p) ?? true).ToHashSet(StringComparer.Ordinal),
        Encounters = Scenarios.Roster.Select(e => e.ClassName).Where(e => encounter?.Invoke(e) ?? true).ToHashSet(StringComparer.Ordinal),
    };

    private static double Percent(int part, int whole) => whole == 0 ? 0 : 100.0 * part / whole;

    private static void Near(Check c, double expected, double actual, double tolerance, string what) =>
        c.That(Math.Abs(expected - actual) <= tolerance, FormattableString.Invariant($"{what}: expected {expected:0.0#} within {tolerance:0.0#}, got {actual:0.00}"));

    private static void Within(Check c, double low, double high, double actual, string what) =>
        c.That(actual >= low && actual <= high, FormattableString.Invariant($"{what}: expected {low:0.0#} to {high:0.0#}, got {actual:0.00}"));

    public void Run(Check c)
    {
        ActLists(c);
        Paths(c);
        UnknownRooms(c);
        CardRewards(c);
        PotionDrops(c);
        ScenariosOnTheFullLists(c);
        LeftOutContent(c);
        TheRegistry(c);
    }

    // ---------- game: the act's lists (1.5, 1.6) ----------

    private static void ActLists(Check c)
    {
        c.Group("the roster is Overgrowth's: 4 weak, 12 regular, 3 elites, 3 bosses, with the game's tags", () =>
        {
            var by = Scenarios.Roster.GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
            c.Equal(4, by[Scenarios.EncounterKind.Weak], "weak");
            c.Equal(12, by[Scenarios.EncounterKind.Regular], "regular");
            c.Equal(3, by[Scenarios.EncounterKind.Elite], "elites");
            c.Equal(3, by[Scenarios.EncounterKind.Boss], "bosses");
            c.Sequence(Scenarios.Roster.Select(e => e.ClassName).OrderBy(n => n, StringComparer.Ordinal), Scenarios.Roster.Select(e => e.ClassName), "in the act's own (alphabetical) order");
            string Tags(string name) => string.Join("+", Scenarios.Roster.Single(e => e.ClassName == name).Tags);
            c.Equal("Mushroom+Slimes", Tags("FlyconidNormal"), "FlyconidNormal's tags");
            c.Equal("Shrinker+Crawler", Tags("OvergrowthCrawlers"), "OvergrowthCrawlers' tags");
            c.Equal("Nibbit", Tags("NibbitsWeak"), "NibbitsWeak's tag");
            c.Equal("", Tags("NibbitsNormal"), "NibbitsNormal has no tag");
            c.Equal(8, Scenarios.Roster.Count(e => e.Tags.Length > 0), "eight encounters carry tags");
        });

        c.Group("the act's lists: no repeats, tags kept apart, fight 4 by the spec's exact odds", () =>
        {
            const int runs = 200_000;
            var rng = RngSet.For(20261010, "check_act_lists");
            var regular = Scenarios.Roster.Where(e => e.Kind == Scenarios.EncounterKind.Regular).Select(e => e.ClassName).ToList();
            var tags = Scenarios.Roster.ToDictionary(e => e.ClassName, e => e);
            var fourth = regular.ToDictionary(n => n, _ => 0);
            var bosses = new Dictionary<string, int>();
            int badWeak = 0, badRegular = 0, badElites = 0, eliteRepeats = 0, taggedNeighbours = 0, nibbitsAfterNibbits = 0, nibbitsThird = 0;
            for (var n = 0; n < runs; n++)
            {
                var lists = Scenarios.BuildActLists(rng);
                var weak = lists.Hallway.Take(3).ToList();
                if (weak.Distinct().Count() != 3 || weak.Any(w => tags[w].Kind != Scenarios.EncounterKind.Weak)) badWeak++;
                var later = lists.Hallway.Skip(3).ToList();
                if (later.Count != 12 || later.Distinct().Count() != 12 || later.Any(r => tags[r].Kind != Scenarios.EncounterKind.Regular)) badRegular++;
                if (lists.Elites.Count != 15 || lists.Elites.Take(3).Distinct().Count() != 3) badElites++;
                for (var i = 1; i < lists.Elites.Count; i++)
                    if (lists.Elites[i] == lists.Elites[i - 1]) eliteRepeats++;
                fourth[lists.Hallway[3]]++;
                bosses[lists.Boss] = bosses.GetValueOrDefault(lists.Boss) + 1;
                for (var i = 1; i < lists.Hallway.Count; i++)
                {
                    if (!tags[lists.Hallway[i]].SharesTagWith(tags[lists.Hallway[i - 1]])) continue;
                    taggedNeighbours++;
                    break;
                }
                if (lists.Hallway[2] != "NibbitsWeak") continue;
                nibbitsThird++;
                if (lists.Hallway[3] == "NibbitsNormal") nibbitsAfterNibbits++;
            }

            c.Equal(0, badWeak, "fights 1 to 3 are three different weak encounters");
            c.Equal(0, badRegular, "fights 4 to 15 are all twelve regular encounters, each once");
            c.Equal(0, badElites, "the first three elites are the three elites");
            c.Equal(0, eliteRepeats, "an elite never follows itself");
            // Exact odds of run-generation.md 1.5: 11/240, 35/528 twice, 241/2640 for the other nine.
            Near(c, 100.0 * 11 / 240, Percent(fourth["OvergrowthCrawlers"], runs), 0.25, "fight 4 is OvergrowthCrawlers (%)");
            Near(c, 100.0 * 35 / 528, Percent(fourth["FlyconidNormal"], runs), 0.25, "fight 4 is FlyconidNormal (%)");
            Near(c, 100.0 * 35 / 528, Percent(fourth["SlimesNormal"], runs), 0.25, "fight 4 is SlimesNormal (%)");
            foreach (var name in regular.Except(["OvergrowthCrawlers", "FlyconidNormal", "SlimesNormal"]))
                Near(c, 100.0 * 241 / 2640, Percent(fourth[name], runs), 0.3, $"fight 4 is {name} (%)");
            Near(c, 3.9, Percent(taggedNeighbours, runs), 0.4, "lists with one neighbouring pair that shares a tag (%)");
            // NibbitsNormal has no tag, so it may directly follow NibbitsWeak.
            Within(c, 7.5, 10.5, Percent(nibbitsAfterNibbits, nibbitsThird), "NibbitsNormal right after NibbitsWeak (%)");
            c.Equal(3, bosses.Count, "every boss occurs");
            foreach (var (boss, count) in bosses) Near(c, 100.0 / 3, Percent(count, runs), 0.5, $"{boss} (%)");
        });
    }

    // ---------- the path (1.2, 1.3) ----------

    private static void Paths(Check c)
    {
        c.Group("a path has the act's fixed floors and keeps its bans; the mix is the measured one", () =>
        {
            const int runs = 100_000;
            var map = RngSet.For(20261010, "check_path_map");
            var unknown = RngSet.For(20261010, "check_path_unknown");
            var broken = 0;
            long monsters = 0, elites = 0, rests = 0, shops = 0, unknowns = 0, fights = 0;
            int noElite = 0;
            for (var n = 0; n < runs; n++)
            {
                var path = Scenarios.BuildPath(map, unknown);
                var ok = path.Count == 16 && path.Select(s => s.Floor).SequenceEqual(Enumerable.Range(2, 16))
                    && path[0] is { Room: Scenarios.Room.Monster, WasUnknown: false }
                    && path[8] is { Floor: 10, Room: Scenarios.Room.Treasure, WasUnknown: false }
                    && path[14] is { Floor: 16, Room: Scenarios.Room.Rest }
                    && path[15] is { Floor: 17, Room: Scenarios.Room.Boss };
                for (var i = 0; ok && i < path.Count; i++)
                {
                    var s = path[i];
                    if (s.Floor < 7 && s.Room is Scenarios.Room.Elite or Scenarios.Room.Rest) ok = false;
                    if (s.Floor is 14 or 15 && s.Room == Scenarios.Room.Rest) ok = false;
                    if (s.Room == Scenarios.Room.Boss && s.Floor != 17) ok = false;
                    if (s.Room == Scenarios.Room.Event && !s.WasUnknown) ok = false;
                    if (i == 0) continue;
                    var p = path[i - 1];
                    // Never two Elites or two Rest Sites in a row; never a Shop node right above a Shop node; and an
                    // Unknown room never turns out a Shop right after a Shop. (A Shop node above an Unknown room
                    // that turned out a Shop is possible in the game too.)
                    if (s.Room == p.Room && s.Room is Scenarios.Room.Elite or Scenarios.Room.Rest) ok = false;
                    if (s.Room == Scenarios.Room.Shop && p.Room == Scenarios.Room.Shop && (s.WasUnknown || !p.WasUnknown)) ok = false;
                }
                // The whole map has three Shop nodes.
                if (path.Count(s => s.Room == Scenarios.Room.Shop && !s.WasUnknown) > 3) ok = false;
                if (!ok) broken++;

                monsters += path.Count(s => s.Room == Scenarios.Room.Monster && !s.WasUnknown);
                fights += path.Count(s => s.Room == Scenarios.Room.Monster);
                unknowns += path.Count(s => s.WasUnknown);
                shops += path.Count(s => s.Room == Scenarios.Room.Shop && !s.WasUnknown);
                rests += path.Count(s => s.Room == Scenarios.Room.Rest);
                var e = path.Count(s => s.Room == Scenarios.Room.Elite);
                elites += e;
                if (e == 0) noElite++;
            }

            c.Equal(0, broken, "paths that break the act's structure");
            // run-generation.md 1.3, per path: 6.8 Monster nodes, 1.1 Elites, 2.5 Rest Sites (the last one included), 0.7 Shops, 2.9 Unknowns.
            Near(c, 6.8, (double)monsters / runs, 0.4, "Monster nodes on a path");
            Near(c, 1.1, (double)elites / runs, 0.2, "Elites on a path");
            Near(c, 2.5, (double)rests / runs, 0.25, "Rest Sites on a path");
            Near(c, 0.7, (double)shops / runs, 0.15, "Shops on a path");
            Near(c, 2.9, (double)unknowns / runs, 0.3, "Unknown rooms on a path");
            // 1.7: 6 to 9 hallway fights on a typical path (Monster nodes plus about half a fight from Unknown rooms).
            Within(c, 6.5, 8.0, (double)fights / runs, "hallway fights on a path");
            Near(c, 22, Percent(noElite, runs), 6, "paths without an elite (%)");
        });
    }

    // ---------- game: Unknown rooms (1.4) ----------

    private static void UnknownRooms(Check c)
    {
        c.Group("Unknown rooms: the k-th one is a fight with 10.0, 19.0, 25.3, 28.3, 28.6%", () =>
        {
            const int runs = 400_000;
            var rng = RngSet.For(20261010, "check_unknown");
            var fights = new int[5];
            var events = new int[5];
            for (var n = 0; n < runs; n++)
            {
                float m = Scenarios.UnknownMonsterOdds, t = Scenarios.UnknownTreasureOdds, s = Scenarios.UnknownShopOdds;
                for (var k = 0; k < 5; k++)
                {
                    var room = Scenarios.RollUnknown(false, ref m, ref t, ref s, rng);
                    if (room == Scenarios.Room.Monster) fights[k]++;
                    if (room == Scenarios.Room.Event) events[k]++;
                }
            }
            double[] fight = [10.0, 19.0, 25.3, 28.3, 28.6];
            double[] ev = [85, 71, 60, 54, 50];
            for (var k = 0; k < 5; k++)
            {
                Near(c, fight[k], Percent(fights[k], runs), 0.3, $"Unknown {k + 1} is a fight (%)");
                Near(c, ev[k], Percent(events[k], runs), 0.8, $"Unknown {k + 1} is an Event (%)");
            }

            // The odds after one Event: 0.20, 0.04, 0.06; the rolled kind goes back to its start.
            float m1 = 0.10f, t1 = 0.02f, s1 = 0.03f;
            var always = RngSet.For(1, "check_unknown_one");
            Scenarios.Room first;
            do
            {
                m1 = 0.10f;
                t1 = 0.02f;
                s1 = 0.03f;
                first = Scenarios.RollUnknown(false, ref m1, ref t1, ref s1, always);
            }
            while (first != Scenarios.Room.Event);
            c.That(Math.Abs(m1 - 0.20f) < 1e-6 && Math.Abs(t1 - 0.04f) < 1e-6 && Math.Abs(s1 - 0.06f) < 1e-6, "after an Event every kind has grown by its starting odds");

            // Right after a Shop no Shop can be rolled, and its odds do not grow.
            var shopsAfterShop = 0;
            var grew = false;
            for (var n = 0; n < 50_000; n++)
            {
                float m = 0.10f, t = 0.02f, s = 0.03f;
                if (Scenarios.RollUnknown(true, ref m, ref t, ref s, rng) == Scenarios.Room.Shop) shopsAfterShop++;
                if (s != 0.03f) grew = true;
            }
            c.Equal(0, shopsAfterShop, "Shops rolled right after a Shop");
            c.That(!grew, "the Shop odds stay as they were when the Shop was barred");
        });
    }

    // ---------- game: card rewards (2.2 to 2.4) ----------

    private static void CardRewards(Check c)
    {
        c.Group("card rarity: the pity offset gives the spec's table for the first five hallway rewards", () =>
        {
            const int runs = 400_000;
            var rng = RngSet.For(20261010, "check_rarity");
            var rare = new int[5, 3];
            var uncommon = new int[5, 3];
            var anyRare = new int[5];
            for (var n = 0; n < runs; n++)
            {
                var offset = Scenarios.BaseRarityOffset;
                for (var reward = 0; reward < 5; reward++)
                {
                    var any = false;
                    for (var card = 0; card < 3; card++)
                    {
                        var rarity = Scenarios.RollRarity(false, ref offset, rng);
                        if (rarity == CardRarity.Rare)
                        {
                            rare[reward, card]++;
                            any = true;
                        }
                        if (rarity == CardRarity.Uncommon) uncommon[reward, card]++;
                    }
                    if (any) anyRare[reward]++;
                }
            }

            // run-generation.md 2.3: the Rare chance of each of the three cards, and of at least one Rare in the offer.
            double[,] rareTable = { { 0, 0, 0 }, { 1.0, 2.0, 2.9 }, { 3.8, 4.5, 5.2 }, { 5.7, 6.2, 6.5 }, { 6.8, 6.9, 7.0 } };
            double[] anyTable = [0, 5.9, 13.5, 18.5, 20.8];
            for (var reward = 0; reward < 5; reward++)
            {
                for (var card = 0; card < 3; card++)
                    Near(c, rareTable[reward, card], Percent(rare[reward, card], runs), 0.15, $"reward {reward + 1}, card {card + 1} is Rare (%)");
                Near(c, anyTable[reward], Percent(anyRare[reward], runs), 0.2, $"reward {reward + 1} shows a Rare (%)");
            }
            c.Equal(0, rare[0, 0] + rare[0, 1] + rare[0, 2], "the first reward of a run never shows a Rare");
            // The negative offset also eats into the Uncommon chance of a run's first cards: 35, 36, 37%.
            Near(c, 35, Percent(uncommon[0, 0], runs), 0.3, "card 1 of a run is Uncommon (%)");
            Near(c, 36, Percent(uncommon[0, 1], runs), 0.3, "card 2 of a run is Uncommon (%)");
            Near(c, 37, Percent(uncommon[0, 2], runs), 0.3, "card 3 of a run is Uncommon (%)");
            Near(c, 36.8, Percent(uncommon[3, 1], runs), 0.4, "later cards are Uncommon (%)");
        });

        c.Group("card rarity: elite odds use the same offset on a 10% base, and a Rare resets it", () =>
        {
            const int rolls = 300_000;
            var rng = RngSet.For(20261010, "check_elite_rarity");
            // At the run's start (-0.05): 5% Rare, 40% Uncommon. After 6 cards that were not Rare: 11%. After 12: 17%.
            foreach (var (cardsBefore, rareWanted) in new[] { (0, 5.0), (6, 11.0), (12, 17.0) })
            {
                int rare = 0, uncommon = 0;
                for (var n = 0; n < rolls; n++)
                {
                    var offset = Scenarios.BaseRarityOffset;
                    for (var i = 0; i < cardsBefore; i++) offset = Math.Min(offset + Scenarios.RarityGrowth, Scenarios.MaxRarityOffset);
                    var rarity = Scenarios.RollRarity(true, ref offset, rng);
                    if (rarity == CardRarity.Rare) rare++;
                    if (rarity == CardRarity.Uncommon) uncommon++;
                }
                Near(c, rareWanted, Percent(rare, rolls), 0.2, $"elite card after {cardsBefore} non-Rare cards is Rare (%)");
                Near(c, 40, Percent(uncommon, rolls), 0.3, $"elite card after {cardsBefore} non-Rare cards is Uncommon (%)");
            }

            // The offset itself: +0.01 after a card that is not Rare, never above 0.4, back to -0.05 after a Rare.
            var one = RngSet.For(3, "check_offset");
            for (var n = 0; n < 2000; n++)
            {
                var before = 0.2f + 0.3f * (n % 2);
                var offset = before;
                var rarity = Scenarios.RollRarity(false, ref offset, one);
                var wanted = rarity == CardRarity.Rare ? -0.05f : Math.Min(before + 0.01f, 0.4f);
                if (offset == wanted) continue;
                c.That(false, FormattableString.Invariant($"offset {before} after a {rarity} became {offset}, not {wanted}"));
                break;
            }
            c.That(true, "the offset moves as the game moves it");
        });

        c.Group("a card offer: three different cards, each card of a rarity as likely as the others, never upgraded in act 1", () =>
        {
            const int offers = 150_000;
            var full = Full();
            var rng = RngSet.For(20261010, "check_offers");
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            int wrongSize = 0, repeats = 0, upgraded = 0, wrongRarity = 0;
            var rarityOf = full.RewardCards.ToDictionary(x => x.ClassName, x => x.Rarity);
            var offset = Scenarios.BaseRarityOffset;
            for (var n = 0; n < offers; n++)
            {
                var offer = Scenarios.RollCardReward(n % 5 == 4, ref offset, rng, full);
                if (offer.Count != 3) wrongSize++;
                if (offer.Select(o => o.ClassName).Distinct().Count() != offer.Count) repeats++;
                foreach (var card in offer)
                {
                    seen[card.ClassName] = seen.GetValueOrDefault(card.ClassName) + 1;
                    if (card.Upgraded) upgraded++;
                    if (rarityOf[card.ClassName] != card.Rarity) wrongRarity++;
                }
            }
            c.Equal(0, wrongSize, "offers that are not three cards");
            c.Equal(0, repeats, "offers that show a card twice");
            c.Equal(0, upgraded, "upgraded reward cards (the chance is 25% times the act's index, which is 0)");
            c.Equal(0, wrongRarity, "cards under a wrong rarity");
            c.Equal(80, seen.Count, "all 80 cards of the pool are offered");
            foreach (var (group, names) in new[] { ("Common", Commons), ("Uncommon", Uncommons), ("Rare", Rares) })
            {
                var counts = names.Select(name => seen.GetValueOrDefault(name)).ToList();
                var mean = counts.Average();
                c.That(counts.All(x => Math.Abs(x - mean) <= 0.08 * mean), FormattableString.Invariant($"every {group} card is offered about as often as the others (mean {mean:0}, from {counts.Min()} to {counts.Max()})"));
            }
        });

        c.Group("a card offer when a rarity has run dry: the next rarity up, Rare wrapping to Common", () =>
        {
            var rng = RngSet.For(20261010, "check_dry");
            var onlyCommons = Full(card: Commons.Contains);
            var onlyTwo = Full(card: name => name is "Anger" or "Inflame");
            var none = Full(card: _ => false);
            var offset = Scenarios.BaseRarityOffset;
            var resets = 0;
            for (var n = 0; n < 5000; n++)
            {
                // Card after card with the offset forced high, so that Rares are rolled often and have to give way.
                offset = 0.4f;
                var offer = Scenarios.RollCardReward(true, ref offset, rng, onlyCommons);
                if (offer.Count != 3 || offer.Any(o => o.Rarity != CardRarity.Common) || offer.Select(o => o.ClassName).Distinct().Count() != 3)
                {
                    c.That(false, "with only Commons in the catalog an offer is three different Commons");
                    break;
                }
                if (offset < 0.39f) resets++;
            }
            c.That(resets > 1000, "the offset still follows the rarity that was rolled, not the card that was given");
            offset = Scenarios.BaseRarityOffset;
            var two = Scenarios.RollCardReward(false, ref offset, rng, onlyTwo);
            c.Sequence(["Anger", "Inflame"], two.Select(o => o.ClassName).OrderBy(x => x, StringComparer.Ordinal), "with two cards in the catalog the offer is those two");
            c.Equal(0, Scenarios.RollCardReward(false, ref offset, rng, none).Count, "with no card in the catalog the offer is empty");
        });
    }

    // ---------- game: potion drops (5.3) ----------

    private static void PotionDrops(Check c)
    {
        c.Group("the potion pool: 48 potions, 16 of each rarity, and the registry accounts for every one", () =>
        {
            var pool = Scenarios.PotionPool;
            c.Equal(48, pool.Count, "potions in the pool");
            c.Equal(48, pool.Select(p => p.ClassName).Distinct().Count(), "different names");
            foreach (var rarity in new[] { PotionRarity.Common, PotionRarity.Uncommon, PotionRarity.Rare })
                c.Equal(16, pool.Count(p => p.Rarity == rarity), $"{rarity} potions");
            c.Sequence(["BloodPotion", "SoldiersStew", "Ashwater"], pool.Take(3).Select(p => p.ClassName), "the Ironclad's own three come first");

            var registry = Content.Registry;
            foreach (var (name, rarity) in pool)
            {
                var has = registry.Has(ContentKind.Potion, name);
                c.That(has || registry.IsExcluded(ContentKind.Potion, name, out _), $"{name} is either in the registry or left out with a reason");
                if (has) c.Equal(rarity, registry.Potion(name).Rarity, $"{name}'s rarity");
            }
            foreach (var name in registry.Potions.Keys)
                c.That(pool.Any(p => p.ClassName == name), $"the registry's {name} is a potion of the Ironclad's pool");
        });

        c.Group("potion drops: 40% at first, 10 points down after a drop and up after none; the spec's table", () =>
        {
            const int runs = 400_000;
            var rng = RngSet.For(20261010, "check_potion_drops");
            int[] at = [1, 2, 3, 4, 6, 8];
            var counts = new int[9, 5];
            var firstDrops = 0;
            var secondAfterDrop = (Drops: 0, Of: 0);
            for (var n = 0; n < runs; n++)
            {
                var odds = Scenarios.BasePotionOdds;
                var drops = 0;
                for (var fight = 1; fight <= 8; fight++)
                {
                    var dropped = Scenarios.RollPotionDrop(false, ref odds, rng);
                    if (fight == 1 && dropped) firstDrops++;
                    if (fight == 2 && drops == 1)
                    {
                        secondAfterDrop.Of++;
                        if (dropped) secondAfterDrop.Drops++;
                    }
                    if (dropped) drops++;
                    counts[fight, Math.Min(drops, 4)]++;
                }
            }

            Near(c, 40, Percent(firstDrops, runs), 0.25, "fight 1 drops a potion (%)");
            Near(c, 30, Percent(secondAfterDrop.Drops, secondAfterDrop.Of), 0.3, "fight 2 drops one after fight 1 did (%)");
            // run-generation.md 5.3: potions dropped after k hallway fights (0, 1, 2, 3, 4 or more), in percent.
            var table = new Dictionary<int, double[]>
            {
                [1] = [60, 40, 0, 0, 0],
                [2] = [30, 58, 12, 0, 0],
                [3] = [12, 53, 33, 2, 0],
                [4] = [4, 35, 49, 12, 0.2],
                [6] = [0.1, 6, 37, 46, 11.5],
                [8] = [0, 0.1, 7, 38, 54],
            };
            foreach (var k in at)
                for (var drops = 0; drops <= 4; drops++)
                    Near(c, table[k][drops], Percent(counts[k, drops], runs), 0.75, $"{drops}{(drops == 4 ? " or more" : "")} potions after {k} fights (%)");

            // An elite adds half of its 25-point bonus: 52.5% at the start of a run.
            var elite = 0;
            for (var n = 0; n < runs; n++)
            {
                var odds = Scenarios.BasePotionOdds;
                if (Scenarios.RollPotionDrop(true, ref odds, rng)) elite++;
            }
            Near(c, 52.5, Percent(elite, runs), 0.25, "an elite at the run's starting odds drops a potion (%)");
        });

        c.Group("which potion drops: 10% Rare, 25% Uncommon, 65% Common, then any of that rarity's 16", () =>
        {
            const int drops = 480_000;
            var full = Full();
            var rng = RngSet.For(20261010, "check_potion_kind");
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var n = 0; n < drops; n++)
            {
                var potion = Scenarios.RollPotion(rng, full)!;
                seen[potion] = seen.GetValueOrDefault(potion) + 1;
            }
            c.Equal(48, seen.Count, "every potion of the pool drops");
            foreach (var (rarity, share) in new[] { (PotionRarity.Common, 65.0), (PotionRarity.Uncommon, 25.0), (PotionRarity.Rare, 10.0) })
            {
                var names = Scenarios.PotionPool.Where(p => p.Rarity == rarity).Select(p => p.ClassName).ToList();
                Near(c, share, Percent(names.Sum(name => seen[name]), drops), 0.25, $"{rarity} (%)");
                foreach (var name in names)
                    Near(c, share / 16, Percent(seen[name], drops), 0.15, $"{name} (%)");
            }

            // A potion the arena lacks is rolled again, rarity and all: the others keep their odds against each other.
            var some = Full(potion: name => name is "FirePotion" or "BlockPotion" or "FyshOil");
            var few = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var n = 0; n < 100_000; n++)
            {
                var potion = Scenarios.RollPotion(rng, some)!;
                few[potion] = few.GetValueOrDefault(potion) + 1;
            }
            c.Sequence(["BlockPotion", "FirePotion", "FyshOil"], few.Keys.OrderBy(x => x, StringComparer.Ordinal), "only the potions of the catalog are dealt");
            Near(c, 1.0, (double)few["FirePotion"] / few["BlockPotion"], 0.05, "two Commons stay equally likely");
            Near(c, 0.65 / 0.25, (double)few["FirePotion"] / few["FyshOil"], 0.12, "a Common stays 2.6 times as likely as an Uncommon");
            c.That(Scenarios.RollPotion(rng, Full(potion: _ => false)) is null, "with no potion in the catalog nothing is dealt");
        });
    }

    // ---------- the scenarios, on the game's full lists ----------

    private static void ScenariosOnTheFullLists(Check c)
    {
        const int perTier = 5000;
        var full = Full();
        var kindOf = Scenarios.Roster.ToDictionary(e => e.ClassName, e => e.Kind);
        var rarityOf = full.RewardCards.ToDictionary(x => x.ClassName, x => x.Rarity);
        var plans = Scenarios.Tiers.ToDictionary(t => t, t => Enumerable.Range(1, perTier).Select(seed => Scenarios.Build(t, seed, full)).ToList());

        c.Group("a scenario is decided by its tier and seed alone", () =>
        {
            foreach (var tier in Scenarios.Tiers)
            {
                foreach (var seed in new[] { 1, 2, 48213, 999_999 })
                {
                    var a = Scenarios.Build(tier, seed, full);
                    var b = Scenarios.Build(tier, seed, full);
                    c.That(a.Summary == b.Summary && a.Deck.SequenceEqual(b.Deck) && a.Potions.SequenceEqual(b.Potions) && a.Hp == b.Hp && a.MaxHp == b.MaxHp
                           && a.Encounter == b.Encounter && a.Floor == b.Floor && a.Path.SequenceEqual(b.Path), $"{tier}-{seed} builds the same twice");
                }
                // The act's first fight is the starter at full HP for everyone, so weak scenarios repeat more.
                c.That(plans[tier].Select(p => p.Summary).Distinct().Count() > perTier * (tier == "weak" ? 0.5 : 0.9), $"{tier}: different seeds give different scenarios");
            }
            c.That(Scenarios.Build("weak", 77, full).Summary != Scenarios.Build("boss", 77, full).Summary, "the tier is part of what a seed means");
        });

        c.Group("each tier is its point of the act: the room, the floor, the fights before it and an encounter of its list", () =>
        {
            foreach (var tier in Scenarios.Tiers)
            {
                var bad = 0;
                foreach (var p in plans[tier])
                {
                    var last = p.Path[^1];
                    var before = p.Path.Take(p.Path.Count - 1).ToList();
                    var ok = last.Floor == p.Floor && p.Path[0].Floor == 2 && p.Path.Count == p.Floor - 1
                        && p.HallwayFightsWon == before.Count(s => s.Room == Scenarios.Room.Monster)
                        && p.ElitesWon == before.Count(s => s.Room == Scenarios.Room.Elite)
                        && p.Tier == tier;
                    ok &= tier switch
                    {
                        "weak" => last.Room == Scenarios.Room.Monster && p.HallwayFightsWon is >= 0 and <= 2 && kindOf[p.Encounter] == Scenarios.EncounterKind.Weak
                                  && (p.HallwayFightsWon == 0) == (p.Floor == 2),
                        "normal" => last.Room == Scenarios.Room.Monster && p.HallwayFightsWon >= 3 && kindOf[p.Encounter] == Scenarios.EncounterKind.Regular && p.Floor >= 5,
                        "elite" => last.Room == Scenarios.Room.Elite && kindOf[p.Encounter] == Scenarios.EncounterKind.Elite && p.Floor is >= 7 and <= 15 and not 10,
                        _ => last.Room == Scenarios.Room.Boss && kindOf[p.Encounter] == Scenarios.EncounterKind.Boss && p.Floor == 17
                             && p.Path[^2] is { Floor: 16, Room: Scenarios.Room.Rest },
                    };
                    if (!ok) bad++;
                }
                c.Equal(0, bad, $"{tier} scenarios that are not at a {tier} point of the act");
            }

            // Which encounter: every one of the tier's, in about the shares the act's lists give.
            foreach (var (tier, kind, low, high) in new[]
                     {
                         ("weak", Scenarios.EncounterKind.Weak, 22.0, 28.0), ("normal", Scenarios.EncounterKind.Regular, 5.5, 11.0),
                         ("elite", Scenarios.EncounterKind.Elite, 30.0, 37.0), ("boss", Scenarios.EncounterKind.Boss, 30.0, 37.0),
                     })
            {
                foreach (var entry in Scenarios.Roster.Where(e => e.Kind == kind))
                    Within(c, low, high, Percent(plans[tier].Count(p => p.Encounter == entry.ClassName), perTier), $"{tier}: {entry.ClassName} (%)");
            }
            Within(c, 28, 39, Percent(plans["weak"].Count(p => p.HallwayFightsWon == 0), perTier), "weak: the act's very first fight (%)");
            Within(c, 4.5, 7.5, plans["normal"].Average(p => p.HallwayFightsWon + 1.0), "normal: which hallway fight of the act, on average");
            Within(c, 60, 90, Percent(plans["elite"].Count(p => p.ElitesWon == 0), perTier), "elite: the act's first elite (%)");
        });

        c.Group("the deck is the starter plus what the fights offered, less what was removed", () =>
        {
            var all = plans.Values.SelectMany(x => x).ToList();
            int bad = 0, offersTotal = 0, taken = 0, upgradedRewards = 0, firstOfferRares = 0;
            int secondCards = 0, secondRares = 0, laterCards = 0, laterUncommons = 0;
            foreach (var p in all)
            {
                var scroll = p.Neow == Scenarios.NeowStart.ArcaneScroll ? 1 : 0;
                var rests = p.Path.Take(p.Path.Count - 1).Count(s => s.Room == Scenarios.Room.Rest);
                var shops = p.Path.Take(p.Path.Count - 1).Count(s => s.Room == Scenarios.Room.Shop);
                var fromOffers = p.Offers.Where(o => o.Taken >= 0).Select(o => o.Cards[o.Taken].ClassName).ToList();
                var ok = p.Deck.Count == 10 + p.Added.Count - p.Removed.Count
                    && p.Offers.Count == p.HallwayFightsWon + p.ElitesWon
                    && p.Added.Count == fromOffers.Count + scroll
                    && p.Added.Skip(scroll).Select(a => a.ClassName).SequenceEqual(fromOffers)
                    && (scroll == 0 || rarityOf[p.Added[0].ClassName] == CardRarity.Rare)
                    && p.Offers.All(o => o.Cards.Count == 3 && o.Cards.Select(x => x.ClassName).Distinct().Count() == 3)
                    && p.Upgraded.Count <= rests + (p.Neow == Scenarios.NeowStart.Pomander ? 1 : 0)
                    && p.Deck.Count(d => d.Upgraded) == p.Upgraded.Count
                    && p.Removed.Count <= Math.Min(shops, 1) + (p.Neow == Scenarios.NeowStart.PreciseScissors ? 1 : 0)
                    && p.Removed.All(r => r is "StrikeIronclad" or "DefendIronclad")
                    && p.Deck.Count(d => d.ClassName == "StrikeIronclad") == 5 - p.Removed.Count(r => r == "StrikeIronclad")
                    && p.Deck.Count(d => d.ClassName == "DefendIronclad") == 4 - p.Removed.Count(r => r == "DefendIronclad")
                    && p.Deck.Count(d => d.ClassName == "Bash") == 1
                    && p.Deck.Take(5 - p.Removed.Count(r => r == "StrikeIronclad")).All(d => d.ClassName == "StrikeIronclad");
                if (!ok) bad++;

                for (var i = 0; i < p.Offers.Count; i++)
                {
                    var o = p.Offers[i];
                    offersTotal++;
                    if (o.Taken >= 0) taken++;
                    upgradedRewards += o.Cards.Count(x => x.Upgraded);
                    if (i == 0) firstOfferRares += o.Cards.Count(x => x.Rarity == CardRarity.Rare);
                    if (i == 1 && !o.Elite)
                    {
                        secondCards += 3;
                        secondRares += o.Cards.Count(x => x.Rarity == CardRarity.Rare);
                    }
                    if (i >= 1 && !o.Elite)
                    {
                        laterCards += 3;
                        laterUncommons += o.Cards.Count(x => x.Rarity == CardRarity.Uncommon);
                    }
                }
            }
            c.Equal(0, bad, "decks that do not add up");
            c.Equal(0, upgradedRewards, "upgraded reward cards in act 1");
            c.Equal(0, firstOfferRares, "Rares in the first offer of a run");
            // The second hallway offer: (1.0 + 2.0 + 2.9) / 3 percent Rare. Later hallway cards: about 36.8% Uncommon.
            Near(c, (1.0 + 2.0 + 2.9) / 3, Percent(secondRares, secondCards), 0.35, "Rare cards in the second offer of a run (%)");
            Near(c, 36.8, Percent(laterUncommons, laterCards), 0.8, "Uncommon cards in hallway offers after the first (%)");
            Near(c, 100 * Scenarios.TakeCardChance, Percent(taken, offersTotal), 1.0, "offers a card was taken from (%)");

            // run-generation.md 8: typical size 10-12 early, 13-16 in the middle, 16-20 late.
            Within(c, 10, 12, plans["weak"].Average(p => (double)p.Deck.Count), "weak: mean deck size");
            Within(c, 13, 16, plans["normal"].Average(p => (double)p.Deck.Count), "normal: mean deck size");
            Within(c, 13, 16, plans["elite"].Average(p => (double)p.Deck.Count), "elite: mean deck size");
            Within(c, 16, 20, plans["boss"].Average(p => (double)p.Deck.Count), "boss: mean deck size");
            // At least the starter less two removals (Precise Scissors and a shop); at most one card per fight won and Neow's scroll.
            c.That(all.All(p => p.Deck.Count >= 8 && p.Deck.Count <= 11 + p.HallwayFightsWon + p.ElitesWon), "every deck has between 8 cards and the starter plus one per fight won plus one");
            c.That(plans["weak"].All(p => p.Deck.Count <= 14), "weak: never more than 14 cards");

            Near(c, 100 * Scenarios.SmithChance, Percent(
                all.Sum(p => p.Upgraded.Count - (p.Neow == Scenarios.NeowStart.Pomander ? 1 : 0)),
                all.Sum(p => p.Path.Take(p.Path.Count - 1).Count(s => s.Room == Scenarios.Room.Rest))), 1.5, "Rest Sites used to upgrade (%)");
            Within(c, 8, 12, Percent(all.Count(p => p.Neow == Scenarios.NeowStart.Pomander), all.Count), "runs that took Pomander (%)");
            Within(c, 8, 12, Percent(all.Count(p => p.Neow == Scenarios.NeowStart.ArcaneScroll), all.Count), "runs that took Arcane Scroll (%)");
            Within(c, 57, 63, Percent(all.Count(p => p.Neow == Scenarios.NeowStart.NoChange), all.Count), "runs whose Neow relic changed nothing here (%)");
        });

        c.Group("HP is in the believable range for the point of the act", () =>
        {
            var bad = 0;
            foreach (var p in plans.Values.SelectMany(x => x))
            {
                var bonus = p.Neow == Scenarios.NeowStart.NutritiousOyster ? 11 : 0;
                var last = p.Path[^1];
                var (min, max) = last.Room == Scenarios.Room.Boss ? (55, 80)
                    : last.Room == Scenarios.Room.Monster && p.HallwayFightsWon == 0 ? (80, 80)
                    : last.Room == Scenarios.Room.Monster && p.HallwayFightsWon < 3 ? (68, 80)
                    : p.Floor <= 10 ? (45, 75)
                    : (35, 65);
                if (p.MaxHp != 80 + bonus || p.Hp < min + bonus || p.Hp > max + bonus || p.Hp > p.MaxHp || p.Hp < 7) bad++;
            }
            c.Equal(0, bad, "scenarios whose HP is outside the range of their point (run-generation.md 6.3)");
            c.That(plans["weak"].Where(p => p.HallwayFightsWon == 0).All(p => p.Hp == p.MaxHp), "the act's first fight is entered at full HP");
            foreach (var (tier, low, high) in new[] { ("weak", 68, 91), ("normal", 35, 86), ("elite", 35, 86), ("boss", 55, 91) })
            {
                var hps = plans[tier].Select(p => p.Hp).ToList();
                c.That(hps.Min() >= low && hps.Max() <= high, $"{tier}: HP from {hps.Min()} to {hps.Max()} lies within {low} to {high}");
                c.That(hps.Distinct().Count() >= 10, $"{tier}: HP varies");
            }
        });

        c.Group("the belt holds what dropped and was not drunk: three slots, the first free one filled first", () =>
        {
            var all = plans.Values.SelectMany(x => x).ToList();
            var pool = Scenarios.PotionPool.ToDictionary(p => p.ClassName, p => p.Rarity);
            c.That(all.All(p => p.Potions.Count == 3), "three slots");
            c.That(all.All(p => p.Potions.All(x => x is null || pool.ContainsKey(x))), "only potions of the pool");
            c.That(all.All(p => p.Potions.Count(x => x is not null) <= Math.Min(3, p.PotionsDropped)), "never more potions than dropped");
            c.That(all.All(p => p.PotionsDropped <= p.HallwayFightsWon + p.ElitesWon), "at most one drop per fight won");
            c.That(plans["weak"].Where(p => p.HallwayFightsWon == 0).All(p => p.Potions.All(x => x is null)), "the act's first fight is entered with an empty belt");

            double Mean(string tier) => plans[tier].Average(p => (double)p.Potions.Count(x => x is not null));
            // run-generation.md 5.3: 0-1 potions for fights 1-3, 1-2 in the middle of the act, 1-3 at an elite or the boss.
            Within(c, 0.2, 1.0, Mean("weak"), "weak: potions held, on average");
            Within(c, 1.0, 2.0, Mean("normal"), "normal: potions held, on average");
            Within(c, 1.0, 2.2, Mean("elite"), "elite: potions held, on average");
            Within(c, 1.0, 2.5, Mean("boss"), "boss: potions held, on average");

            var held = all.SelectMany(p => p.Potions).Where(x => x is not null).Select(x => pool[x!]).ToList();
            Near(c, 65, Percent(held.Count(r => r == PotionRarity.Common), held.Count), 1.5, "Common potions among those held (%)");
            Near(c, 10, Percent(held.Count(r => r == PotionRarity.Rare), held.Count), 1.0, "Rare potions among those held (%)");
        });
    }

    // ---------- what the arena leaves out ----------

    private static void LeftOutContent(Check c)
    {
        c.Group("a card, potion or encounter that is not in the catalog is never dealt, and the odds of the rest stay", () =>
        {
            const int runs = 2000;
            // Every second card and potion is missing, and every encounter but one of each kind.
            var missingCards = Commons.Concat(Uncommons).Concat(Rares).Where((_, i) => i % 2 == 1).ToHashSet(StringComparer.Ordinal);
            var missingPotions = Scenarios.PotionPool.Select(p => p.ClassName).Where((_, i) => i % 2 == 1).ToHashSet(StringComparer.Ordinal);
            string[] kept = ["SlimesWeak", "RubyRaidersNormal", "PhrogParasiteElite", "TheKinBoss"];
            var partial = Full(card: n => !missingCards.Contains(n), potion: n => !missingPotions.Contains(n), encounter: kept.Contains);
            var full = Full();

            int dealtMissing = 0, rares = 0, cards = 0, fullRares = 0, fullCards = 0;
            foreach (var tier in Scenarios.Tiers)
            {
                for (var seed = 1; seed <= runs; seed++)
                {
                    var p = Scenarios.Build(tier, seed, partial);
                    if (p.Deck.Any(d => missingCards.Contains(d.ClassName)) || p.Offers.Any(o => o.Cards.Any(x => missingCards.Contains(x.ClassName)))
                        || p.Potions.Any(x => x is not null && missingPotions.Contains(x)) || !kept.Contains(p.Encounter)) dealtMissing++;
                    foreach (var o in p.Offers.Where(o => !o.Elite))
                    {
                        cards += o.Cards.Count;
                        rares += o.Cards.Count(x => x.Rarity == CardRarity.Rare);
                    }
                    foreach (var o in Scenarios.Build(tier, seed, full).Offers.Where(o => !o.Elite))
                    {
                        fullCards += o.Cards.Count;
                        fullRares += o.Cards.Count(x => x.Rarity == CardRarity.Rare);
                    }
                }
            }
            c.Equal(0, dealtMissing, "scenarios that deal something the catalog lacks");
            Near(c, Percent(fullRares, fullCards), Percent(rares, cards), 0.6, "Rare cards in hallway offers, with half the pool missing (%)");
        });

        c.Group("a tier with no encounter is an error that names the tier and its encounters", () =>
        {
            var noElites = Full(encounter: name => !name.EndsWith("Elite", StringComparison.Ordinal));
            try
            {
                Scenarios.Build("elite", 1, noElites);
                c.That(false, "an elite scenario was built without any elite");
            }
            catch (InvalidOperationException ex)
            {
                c.That(ex.Message.Contains("'elite'", StringComparison.Ordinal) && ex.Message.Contains("ByrdonisElite", StringComparison.Ordinal)
                       && ex.Message.Contains("BygoneEffigyElite", StringComparison.Ordinal) && ex.Message.Contains("PhrogParasiteElite", StringComparison.Ordinal),
                    $"the message names the tier and the missing encounters: {ex.Message}");
            }
            c.That(Scenarios.Build("weak", 1, noElites).Encounter.EndsWith("Weak", StringComparison.Ordinal), "the other tiers still work");
            try
            {
                Scenarios.Build("hard", 1, Full());
                c.That(false, "an unknown tier was accepted");
            }
            catch (ArgumentException)
            {
                c.That(true, "an unknown tier is refused");
            }
        });
    }

    // ---------- the generator on the registry as it is ----------

    private static void TheRegistry(Check c)
    {
        var registry = Content.Registry;
        var catalog = Scenarios.RegistryCatalog;
        var available = Scenarios.Tiers.Where(tier => catalog.Has(tier switch
        {
            "weak" => Scenarios.EncounterKind.Weak,
            "normal" => Scenarios.EncounterKind.Regular,
            "elite" => Scenarios.EncounterKind.Elite,
            _ => Scenarios.EncounterKind.Boss,
        })).ToList();

        c.Group("the registry has an encounter for every tier, under the act's names and kinds", () =>
        {
            foreach (var tier in Scenarios.Tiers)
                c.That(available.Contains(tier), $"an encounter for the tier '{tier}' is registered");
            foreach (var entry in Scenarios.Roster.Where(e => registry.Has(ContentKind.Encounter, e.ClassName)))
            {
                var encounter = registry.NewEncounter(entry.ClassName);
                var kind = encounter.RoomType switch
                {
                    RoomType.Elite => Scenarios.EncounterKind.Elite,
                    RoomType.Boss => Scenarios.EncounterKind.Boss,
                    _ => encounter.IsWeak ? Scenarios.EncounterKind.Weak : Scenarios.EncounterKind.Regular,
                };
                c.Equal(entry.Kind, kind, $"{entry.ClassName} is registered as the kind the act has it as");
            }
            c.That(catalog.RewardCards.All(x => x.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare), "reward cards are Common, Uncommon or Rare");
            c.That(catalog.RewardCards.All(x => x.ClassName is not ("Tank" or "DemonicShield")), "no multiplayer-only card is a reward");
        });

        c.Group("the tier pattern: 2 weak, 4 normal, 2 elite and 2 boss in every 10", () =>
        {
            // The split Mark asked for. Whoever changes Scenarios.TierPattern changes these numbers with it.
            c.Equal(10, Scenarios.TierPattern.Length, "the pattern's length");
            c.Equal(2, Scenarios.TierPattern.Count(t => t == "weak"), "weak");
            c.Equal(4, Scenarios.TierPattern.Count(t => t == "normal"), "normal");
            c.Equal(2, Scenarios.TierPattern.Count(t => t == "elite"), "elite");
            c.Equal(2, Scenarios.TierPattern.Count(t => t == "boss"), "boss");
            c.Equal(4, Scenarios.TierPattern.Take(4).Distinct().Count(), "the first four scenarios are one of each tier");
        });

        if (available.Count < Scenarios.Tiers.Length)
        {
            c.Group("Generate says which tier it cannot make", () =>
            {
                try
                {
                    Scenarios.Generate(10, 1);
                    c.That(false, "Generate ran although a tier has no encounter");
                }
                catch (InvalidOperationException ex)
                {
                    c.That(Scenarios.Tiers.Except(available).Any(tier => ex.Message.Contains($"'{tier}'", StringComparison.Ordinal)), $"the error names the tier: {ex.Message}");
                }
            });
        }
        else
        {
            c.Group("Generate: the same list for the same seed, a longer list starts with the shorter one, tiers by the pattern", () =>
            {
                var thirty = Scenarios.Generate(30, 5);
                c.Sequence(thirty, Scenarios.Generate(30, 5), "the same seed gives the same list");
                c.Sequence(thirty.Take(12), Scenarios.Generate(12, 5), "Generate(30) starts with Generate(12)");
                c.Sequence(thirty, Scenarios.Generate(47, 5).Take(30), "Generate(47) starts with Generate(30)");
                c.Sequence(Enumerable.Range(0, 30).Select(i => Scenarios.TierPattern[i % 10]), thirty.Select(s => s.Tier), "tiers follow the pattern");
                c.Equal(30, thirty.Select(s => s.Id).Distinct().Count(), "ids are different");
                c.That(!thirty.Select(s => s.Id).SequenceEqual(Scenarios.Generate(30, 6).Select(s => s.Id)), "another seed gives another list");
                c.Equal(0, Scenarios.Generate(0, 5).Count, "none when none are asked for");

                var id = new Regex("^(weak|normal|elite|boss)-[1-9][0-9]{0,5}$");
                foreach (var s in thirty)
                {
                    c.That(id.IsMatch(s.Id) && s.Id == $"{s.Tier}-{s.Seed}", $"{s.Id} is the tier and the seed");
                    c.That(Scenarios.FromId(s.Id) == s, $"FromId rebuilds {s.Id}");
                    c.That(s.Summary.StartsWith("floor ", StringComparison.Ordinal) && s.Summary.Contains(" HP, ", StringComparison.Ordinal)
                           && s.Summary.Contains(" cards (", StringComparison.Ordinal) && s.Summary.Contains(" vs ", StringComparison.Ordinal), $"{s.Id}'s summary has floor, HP, deck and encounter: {s.Summary}");
                }
            });
        }

        c.Group("FromId refuses what is not an id", () =>
        {
            foreach (var bad in new string?[] { null, "", "weak", "weak-", "-5", "weak-x", "hard-5", "weak-5-", "weak--5", "weak-1.5", "WEAK-5", " weak-5", "weak-0", "weak-007", "weak-1000000", "weak-99999999999" })
            {
                try
                {
                    Scenarios.FromId(bad!);
                    c.That(false, $"'{bad}' was accepted as an id");
                }
                catch (ArgumentException)
                {
                    c.That(true, $"'{bad}' is refused");
                }
            }
        });

        c.Group("nothing the arena leaves out is ever dealt, and every scenario starts as its fight", () =>
        {
            const int perTier = 400;
            foreach (var tier in available)
            {
                var problems = new List<string>();
                for (var seed = 1; seed <= perTier && problems.Count < 3; seed++)
                {
                    var scenario = Scenarios.FromId($"{tier}-{seed}");
                    var setup = Scenarios.Setup(scenario);
                    var again = Scenarios.Setup(Scenarios.FromId(scenario.Id));
                    void Bad(string what) => problems.Add($"{scenario.Id}: {what}");

                    if (!setup.Deck.SequenceEqual(again.Deck) || !setup.Potions.SequenceEqual(again.Potions) || setup.Hp != again.Hp || setup.Encounter != again.Encounter) Bad("FromId rebuilds a different fight");
                    foreach (var card in setup.Deck)
                    {
                        if (!registry.Has(ContentKind.Card, card.ClassName) || registry.IsExcluded(ContentKind.Card, card.ClassName, out _)) Bad($"deals the card {card.ClassName}, which the arena does not have");
                        else if (card.Upgraded && registry.Card(card.ClassName).MaxUpgradeLevel < 1) Bad($"upgrades {card.ClassName}, which cannot be upgraded");
                    }
                    if (setup.Potions.Count != 3) Bad("the belt does not have three slots");
                    foreach (var potion in setup.Potions.Where(p => p is not null))
                        if (!registry.Has(ContentKind.Potion, potion!) || registry.IsExcluded(ContentKind.Potion, potion!, out _)) Bad($"deals the potion {potion}, which the arena does not have");
                    if (!registry.Has(ContentKind.Encounter, setup.Encounter)) Bad($"is against {setup.Encounter}, which the arena does not have");
                    if (setup.Hp < 1 || setup.Hp > setup.MaxHp || setup.MaxHp is not (80 or 91)) Bad($"has {setup.Hp}/{setup.MaxHp} HP");
                    if (setup.Seed != seed || setup.Floor is < 2 or > 17) Bad($"has seed {setup.Seed} and floor {setup.Floor}");
                    if (problems.Count > 0 || seed > 40) continue;

                    // The fight itself, for the first few: what the scenario says is what the fight starts with.
                    Fight fight;
                    try
                    {
                        fight = new Fight(FightConfig.From(setup));
                    }
                    catch (Exception ex)
                    {
                        Bad($"the fight against {setup.Encounter} cannot start ({ex.GetType().Name}: {ex.Message})");
                        continue;
                    }
                    var wantedRoom = tier switch { "elite" => RoomType.Elite, "boss" => RoomType.Boss, _ => RoomType.Monster };
                    if (fight.RoomType != wantedRoom) Bad($"is a {fight.RoomType} room");
                    if (fight.Player.Hp != setup.Hp || fight.Player.MaxHp != setup.MaxHp) Bad("starts with other HP than the scenario says");
                    if (fight.Hand.Count + fight.DrawPile.Count != setup.Deck.Count) Bad("starts with another deck size than the scenario says");
                    if (!fight.Potions.Select(p => p?.ClassName).SequenceEqual(setup.Potions)) Bad("starts with another belt than the scenario says");
                    if (fight.Floor != setup.Floor || fight.Seed != seed) Bad("starts on another floor or seed");
                    var lineUp = string.Join(" + ", fight.Roster.Select(e => e.Name));
                    if (!scenario.Summary.EndsWith($" vs {lineUp} ({setup.Encounter})", StringComparison.Ordinal)) Bad($"summary does not end with its line-up '{lineUp}': {scenario.Summary}");
                    // " + " joins monsters and nothing else: the self-check finds a fight with several enemies by it.
                    if (scenario.Summary.Contains(" + ", StringComparison.Ordinal) != fight.Roster.Count > 1) Bad($"has ' + ' in its summary for another reason than several enemies: {scenario.Summary}");
                    if (!scenario.Summary.Contains($"{setup.Hp}/{setup.MaxHp} HP, {setup.Deck.Count} cards (", StringComparison.Ordinal)) Bad($"summary does not give HP and deck size: {scenario.Summary}");
                    foreach (var potion in setup.Potions.Where(p => p is not null))
                        if (!scenario.Summary.Contains(registry.Potion(potion!).Name, StringComparison.Ordinal)) Bad($"summary does not name the potion {potion}");
                }
                c.That(problems.Count == 0, problems.Count == 0 ? $"{tier}: {perTier} scenarios deal only what the arena has" : string.Join(" | ", problems));
            }
        });
    }
}
