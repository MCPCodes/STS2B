using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// Checks of the start of a run: the seed and its hash, the streams, the game's lists, the
/// relic bags, the act lists and the three odds. The evidence is the real game's: six saves of
/// v0.107.1 (<see cref="RunGoldenData"/>) and the record the game itself wrote of one of
/// those runs (two fights, an Unknown room, a shop). Numbers that are not taken from a save
/// were worked out by hand from the game's code, the stream values with a second, separate
/// implementation of the generator that was first held against the same saves.
/// </summary>
public sealed class RunStreamsChecks : IArenaChecks
{
    /// <summary>The seed of the save that was played on for three rooms, and of the game's record of that run.</summary>
    private const string PlayedSeed = "1BUSLN0FY5";

    public void Run(Check c)
    {
        SeedAndNames(c);
        Lists(c);
        Streams(c);
        KeyedStreams(c);
        FightStreamsCheck(c);
        RarityOdds(c);
        PotionAndUnknownOdds(c);
        RealRunRolls(c);
        Bags(c);
        ActRoll(c);
        GoldenSaves(c);
        GenerationRules(c);
        Unlocks(c);
        World(c);
        FightsOfARun(c);
    }

    // ---------- the seed, the hash, the two spellings ----------

    private static void SeedAndNames(Check c)
    {
        c.Group("the seed number is the game's hash of the seed text (five real saves)", () =>
        {
            // players[0].rng.seed of each save: the seed number plus the player's slot, 0.
            (string Seed, uint Number)[] saved =
            [
                ("KMEHXZW0R5", 4291969553), ("EEXRY5WA1R", 4218543467), ("F30CH975SF", 2474876250), ("E9U400ERJ4", 3416323072), ("1BUSLN0FY5", 4029914673),
            ];
            foreach (var (seed, number) in saved) c.Equal(number, new RunSeed(seed).Hash, $"hash of {seed}");
        });

        c.Group("a typed seed: capitals, O read as 0, I as 1, no spaces around it", () =>
        {
            c.Equal("KMEHXZW0R5", new RunSeed("  kmehxzwor5 ").Text, "lower case, a letter O and spaces");
            c.Equal("1BUSLN0FY5", new RunSeed("ibuslnofy5").Text, "a letter I and a letter O");
            c.Equal(new RunSeed("1BUSLN0FY5").Hash, new RunSeed("ibuslnofy5").Hash, "the same run either way");
            c.Equal(34, RunSeed.Alphabet.Length, "the game's alphabet: 10 digits and 24 letters");
            c.That(!RunSeed.Alphabet.Contains('I') && !RunSeed.Alphabet.Contains('O'), "no I and no O in it");
            var made = RunSeed.Random(new GameRng(5));
            c.Equal(10, made.Text.Length, "a seed made here has 10 characters");
            c.That(made.Text.All(ch => RunSeed.Alphabet.Contains(ch)), "all from the alphabet");
            c.Equal(made.Text, RunSeed.Canonical(made.Text), "and it is a seed a player can type as it stands");
        });

        c.Group("stream names are the enum members in snake case, as a save's counters name them", () =>
        {
            // The keys of rng.counters and players[0].rng.counters of a save, in the save's order.
            string[] run =
            [
                "up_front", "shuffle", "unknown_map_point", "combat_card_generation", "combat_potion_generation", "combat_card_selection",
                "combat_energy_costs", "combat_targets", "monster_ai", "niche", "combat_orbs", "treasure_room_relics",
            ];
            string[] player = ["rewards", "shops", "transformations"];
            c.Sequence(run.Concat(player), Enum.GetValues<RunStream>().Select(RunStreams.NameOf), "the fifteen streams");
            c.Equal(3, Enum.GetValues<RunStream>().Count(RunStreams.IsPlayerStream), "three of them are the player's");
            var save = GoldenSave.Parse(RunGoldenData.Saves[0].Json);
            c.Sequence(run.Concat(player).OrderBy(n => n, StringComparer.Ordinal), save.Counters.Keys.OrderBy(n => n, StringComparer.Ordinal), "the save's own counter names");
        });

        c.Group("snake case and id entries follow the game's pattern", () =>
        {
            c.Equal("monster_ai", GameNames.SnakeCase("MonsterAi"), "MonsterAi");
            c.Equal("treasure_room_relics", GameNames.SnakeCase("TreasureRoomRelics"), "TreasureRoomRelics");
            // A run of capitals, traced by hand through the pattern: the first pair "AB" matches as a
            // letter and a capital; "C" then matches alone, because it stands where that match ended.
            c.Equal("a_b_cd", GameNames.SnakeCase("ABCd"), "ABCd");
            // Here "BC" is itself a letter and a capital, and that alternative is tried first.
            c.Equal("x_ab_c", GameNames.SnakeCase("XABC"), "XABC");
            c.Equal("THE_KIN_BOSS", GameNames.Slug("TheKinBoss"), "TheKinBoss");
            c.Equal("CHEMICAL_X", GameNames.Slug("ChemicalX"), "ChemicalX");
            c.Equal("BOOK_OF_FIVE_RINGS", GameNames.Slug("BookOfFiveRings"), "BookOfFiveRings");
            c.Equal("A_B", GameNames.Slug("a  b!"), "spaces become one underscore, other signs are dropped");
            c.Equal("TheKinBoss", GameNames.ClassNameOf("ENCOUNTER.THE_KIN_BOSS", ["VantomBoss", "TheKinBoss"]), "an id back to its class name");
            c.Equal(null, GameNames.ClassNameOf("ENCOUNTER.DOORMAKER_BOSS", GameLists.Acts.SelectMany(a => a.Encounters).Select(e => e.ClassName)), "an encounter of an older build is not guessed at");
        });
    }

    // ---------- the game's lists ----------

    private static void Lists(Check c)
    {
        c.Group("the shared relic pool: 118 relics, a list per rarity in the order the rarities first appear", () =>
        {
            c.Equal(118, GameLists.SharedRelics.Count, "relics");
            c.Equal(118, GameLists.SharedRelics.Select(r => r.ClassName).Distinct().Count(), "all different");
            int Of(RelicRarity rarity) => GameLists.SharedRelics.Count(r => r.Rarity == rarity);
            c.Equal(25, Of(RelicRarity.Common), "Common");
            c.Equal(30, Of(RelicRarity.Uncommon), "Uncommon");
            c.Equal(35, Of(RelicRarity.Rare), "Rare");
            c.Equal(25, Of(RelicRarity.Shop), "Shop");
            c.Sequence(["FresnelLens"], GameLists.SharedRelics.Where(r => r.Rarity == RelicRarity.Event).Select(r => r.ClassName), "the Event-rarity list");
            c.Sequence(["LoomingFruit", "VeryHotCocoa"], GameLists.SharedRelics.Where(r => r.Rarity == RelicRarity.Ancient).Select(r => r.ClassName), "the Ancient-rarity list");
            c.Sequence(GameLists.SharedRelics.Select(r => r.ClassName).OrderBy(n => n, StringComparer.Ordinal), GameLists.SharedRelics.Select(r => r.ClassName), "the pool is in alphabetical order");
            // Akabeko (Uncommon), Amethyst Aubergine (Common), Art of War (Rare), Belt Buckle (Shop), Fresnel Lens, Looming Fruit.
            c.Sequence(
                [RelicRarity.Uncommon, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Shop, RelicRarity.Event, RelicRarity.Ancient],
                GameLists.SharedRelics.Select(r => r.Rarity).Distinct(), "the rarities in order of first appearance, which is the order of a save's bag");
            c.Equal(15, GameLists.SharedRelics.Count(r => r.Epoch is not null), "five epochs unlock three shared relics each");
        });

        c.Group("the Ironclad's pool and the player's bag: 122 relics (26 Common, 32 Uncommon, 38 Rare, 26 Shop)", () =>
        {
            var own = GameLists.CharacterRelics["Ironclad"];
            c.Sequence(["Brimstone", "BurningBlood", "CharonsAshes", "DemonTongue", "PaperPhrog", "RedSkull", "RuinedHelmet", "SelfFormingClay"], own.Select(r => r.ClassName), "the Ironclad's eight");
            c.Equal(RelicRarity.Starter, own.Single(r => r.ClassName == "BurningBlood").Rarity, "Burning Blood is the starter");
            c.Equal(5, GameLists.CharacterRelics.Count, "five characters");
            c.That(GameLists.CharacterRelics.Values.All(p => p.Count == 8 && p.Count(r => r.Rarity == RelicRarity.Starter) == 1 && p.Count(r => r.Epoch is not null) == 6),
                "each has 8 relics, one of them the starter, six behind two epochs");
            var pool = RelicBag.PlayerPool(RunUnlocks.All, "Ironclad");
            c.Equal(122, pool.Count, "relics in the bag");
            int Of(RelicRarity rarity) => pool.Count(r => GameLists.BagRelic(r).Rarity == rarity);
            c.Equal(26, Of(RelicRarity.Common), "Common");
            c.Equal(32, Of(RelicRarity.Uncommon), "Uncommon");
            c.Equal(38, Of(RelicRarity.Rare), "Rare");
            c.Equal(26, Of(RelicRarity.Shop), "Shop");
            c.Sequence(["Brimstone", "CharonsAshes", "DemonTongue", "PaperPhrog", "RedSkull", "RuinedHelmet", "SelfFormingClay"], pool.Skip(115), "the Ironclad's seven stand behind the shared 115");
        });

        c.Group("the acts: Overgrowth and Underdocks are first acts, each act's events, encounters and Ancients", () =>
        {
            c.Sequence(["Overgrowth", "Underdocks", "Hive", "Glory"], GameLists.Acts.Select(a => a.ClassName), "the acts in the game's order");
            c.Sequence(["Overgrowth", "Underdocks"], GameLists.ActsByIndex[0].Select(a => a.ClassName), "first acts");
            c.Sequence(["Hive"], GameLists.ActsByIndex[1].Select(a => a.ClassName), "second acts");
            c.Sequence(["Glory"], GameLists.ActsByIndex[2].Select(a => a.ClassName), "third acts");
            c.Equal(18, GameLists.SharedEvents.Count, "shared events");
            // act, own events, encounters, weak, regular, elites, bosses, weak entries of the list, rooms
            (string Act, int Events, int Encounters, int Weak, int Regular, int Elites, int Bosses, int WeakEntries, int Rooms)[] wanted =
            [
                ("Overgrowth", 13, 22, 4, 12, 3, 3, 3, 15), ("Underdocks", 10, 20, 4, 10, 3, 3, 3, 15),
                ("Hive", 10, 20, 4, 10, 3, 3, 2, 14), ("Glory", 7, 18, 3, 9, 3, 3, 2, 13),
            ];
            foreach (var w in wanted)
            {
                var act = GameLists.Act(w.Act);
                c.Equal(w.Events, act.Events.Count, $"{w.Act} events");
                c.Equal(w.Encounters, act.Encounters.Count, $"{w.Act} encounters");
                c.Equal(w.Weak, act.Weak.Count(), $"{w.Act} weak encounters");
                c.Equal(w.Regular, act.Regular.Count(), $"{w.Act} regular encounters");
                c.Equal(w.Elites, act.Elites.Count(), $"{w.Act} elites");
                c.Equal(w.Bosses, act.Bosses.Count(), $"{w.Act} bosses");
                c.Equal(w.WeakEntries, act.WeakEncounters, $"{w.Act} weak entries in the hallway list");
                c.Equal(w.Rooms, act.Rooms, $"{w.Act} rooms");
                c.Sequence(act.Bosses.Select(b => b.ClassName).OrderBy(n => n, StringComparer.Ordinal), act.BossDiscoveryOrder.OrderBy(n => n, StringComparer.Ordinal), $"{w.Act}: the discovery order names the act's three bosses");
            }
            c.Sequence(["VantomBoss", "CeremonialBeastBoss", "TheKinBoss"], GameLists.Act("Overgrowth").BossDiscoveryOrder, "Overgrowth's discovery order");
            c.Sequence(["Neow"], GameLists.Act("Overgrowth").Ancients.Select(a => a.ClassName), "Overgrowth's Ancient");
            c.Sequence(["Orobas", "Pael", "Tezcatara"], GameLists.Act("Hive").Ancients.Select(a => a.ClassName), "Hive's Ancients");
            c.Sequence(["Darv"], GameLists.SharedAncients.Select(a => a.ClassName), "the one shared Ancient");
            c.Equal(57, GameLists.Epochs.Count, "epochs");
            c.That(GameLists.Act("Overgrowth").Events.Contains("SunkenStatue") && GameLists.Act("Underdocks").Events.Contains("SunkenStatue"), "Sunken Statue is in both first acts");
        });

        c.Group("Overgrowth's encounters are the ones the fights were built from, in the same order", () =>
        {
            var act = GameLists.Act("Overgrowth");
            c.Sequence(Scenarios.Roster.Select(e => e.ClassName), act.Encounters.Select(e => e.ClassName), "the scenario generator's roster");
            foreach (var entry in Scenarios.Roster)
            {
                var mine = act.Encounters.Single(e => e.ClassName == entry.ClassName);
                var kind = mine.Room switch
                {
                    RoomType.Elite => Scenarios.EncounterKind.Elite,
                    RoomType.Boss => Scenarios.EncounterKind.Boss,
                    _ => mine.IsWeak ? Scenarios.EncounterKind.Weak : Scenarios.EncounterKind.Regular,
                };
                c.Equal(entry.Kind, kind, $"{entry.ClassName} kind");
                c.Sequence(entry.Tags, mine.Tags, $"{entry.ClassName} tags");
                // And the encounter classes themselves, which were written from the game's files one by one.
                var built = Content.Registry.NewEncounter(entry.ClassName);
                c.That(built.RoomType == mine.Room && built.IsWeak == mine.IsWeak, $"{entry.ClassName}: the registered encounter has the same room type and weak flag");
            }
        });
    }

    // ---------- the streams ----------

    private static void Streams(Check c)
    {
        c.Group("each stream is seeded as the game seeds it (first numbers from a second implementation)", () =>
        {
            var streams = new RunStreams(PlayedSeed);
            c.Equal(4029914673u, streams.Seed.Hash, "the seed number");
            // hash("up_front") = -1234747045, hash("rewards") = -459048038, added in 32 bits.
            c.Equal(unchecked(4029914673u + (uint)-1234747045), RunStreams.LongSeed(streams.Seed, RunStream.UpFront), "up_front: seed number plus the hash of its name");
            c.Equal(unchecked(4029914673u + (uint)-459048038), RunStreams.LongSeed(streams.Seed, RunStream.Rewards), "rewards: the same with the player's slot 0 added");
            c.Equal(8379179105872120628UL, streams.Long(RunStream.UpFront).Clone().NextU64(), "first number of up_front");
            c.Equal(1079328471603807719UL, streams.Long(RunStream.Rewards).Clone().NextU64(), "first number of rewards");
            c.Equal(7317197846264745384UL, RunStreams.ActSelection(streams.Seed).NextU64(), "first number of act_selection");
            c.Equal(5942054735067056084UL, streams.ActMap(1).NextU64(), "first number of act_1_map");
            c.That(streams.ActMap(1).NextU64() != streams.ActMap(2).NextU64(), "every act has its own map stream");
            c.Equal(5942054735067056084UL, streams.ActMap(1).NextU64(), "and asking again gives a new stream from its start");

            // The encounter's stream: seed number + total floor + hash of the id, as one 32-bit sum.
            // 4029914673 + 2 + hash("SHRINKER_BEETLE_WEAK") (1204062005) = 939009384 after the wrap.
            var encounter = streams.Encounter("ShrinkerBeetleWeak", totalFloor: 2);
            c.Equal(9602888757974343678UL, encounter.NextU64(), "Shrinker Beetle on floor 2, first number");
            c.Equal(316582230497344341UL, encounter.NextU64(), "second number");
            c.That(streams.Encounter("ShrinkerBeetleWeak", 3).NextU64() != 9602888757974343678UL, "another floor is another stream");
            // The event's stream: seed number + slot 0 + hash("THIS_OR_THAT") (-460148326) = 3569766347.
            c.Equal(13296873866829451789UL, streams.Event("ThisOrThat").NextU64(), "This or That, first number");
            c.Equal(13296873866829451789UL, streams.Event("ThisOrThat").NextU64(), "the same whenever the event is met");
        });

        c.Group("game mode: a draw is the game's long stream, whatever the purpose", () =>
        {
            var streams = new RunStreams(PlayedSeed);
            c.Equal(StreamMode.Game, streams.Mode, "the default mode");
            var a = streams.Draw(RunStream.Rewards, Purpose.PotionChance);
            var b = streams.Draw(RunStream.Rewards, Purpose.Card(0, 2));
            c.That(ReferenceEquals(a, b) && ReferenceEquals(a, streams.Long(RunStream.Rewards)), "one stream for every purpose");
            a.NextFloat();
            b.NextInt(5);
            c.Equal(2, streams.Counter(RunStream.Rewards), "the counter is what a save would write");
            c.Equal(0, streams.Counter(RunStream.Shops), "other streams did not move");

            var copy = streams.Clone();
            copy.Draw(RunStream.Rewards, Purpose.Gold).NextFloat();
            c.Equal(2, streams.Counter(RunStream.Rewards), "a copy draws for itself");
            var loaded = new RunStreams(PlayedSeed);
            loaded.FastForward(RunStream.Rewards, 2);
            c.Equal(streams.Draw(RunStream.Rewards, Purpose.Gold).NextU64(), loaded.Long(RunStream.Rewards).NextU64(), "a stream moved on to a save's counter gives the number that comes next");

            var threw = false;
            try { streams.FastForward(RunStream.Rewards, 1); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a stream cannot be moved back");
        });
    }

    private static void KeyedStreams(Check c)
    {
        c.Group("keyed mode: a roll's numbers depend on the seed, the place and the purpose, and on nothing done before", () =>
        {
            var one = new RunStreams(PlayedSeed, StreamMode.Keyed);
            var two = new RunStreams(PlayedSeed, StreamMode.Keyed);

            // Player one fought a long fight and looked at a shop before; player two did nothing.
            one.MoveTo(0, 1, 3);
            for (var i = 0; i < 40; i++) one.Draw(RunStream.Shuffle, Purpose.Fight(RunStream.Shuffle)).NextU64();
            one.Draw(RunStream.Rewards, Purpose.PotionChance).NextFloat();
            one.MoveTo(0, 2, 3);
            one.Draw(RunStream.Shops, Purpose.ShopCard(1)).NextU64();

            one.MoveTo(0, 5, 2);
            two.MoveTo(0, 5, 2);
            c.Equal(two.Draw(RunStream.Rewards, Purpose.Gold).NextU64(), one.Draw(RunStream.Rewards, Purpose.Gold).NextU64(), "the same gold roll on the same node");
            // Whether a potion dropped does not shift the cards: player one draws the potion, player two does not.
            one.Draw(RunStream.Rewards, Purpose.Potion()).NextU64();
            for (var card = 0; card < 3; card++)
                c.Equal(two.Draw(RunStream.Rewards, Purpose.Card(0, card)).NextU64(), one.Draw(RunStream.Rewards, Purpose.Card(0, card)).NextU64(), $"the same number for card {card + 1}");

            c.Equal(0, one.Counter(RunStream.Rewards), "the long streams are not touched");
            c.That(!ReferenceEquals(one.Draw(RunStream.UnknownMapPoint, Purpose.UnknownRoom), one.Long(RunStream.UnknownMapPoint)), "a keyed draw is not the long stream");
            c.That(ReferenceEquals(one.Draw(RunStream.UpFront, Purpose.Of("anything")), one.Long(RunStream.UpFront)), "up_front is the long stream in both modes: what it makes belongs to the seed");
        });

        c.Group("keyed mode: every place and purpose has its own stream", () =>
        {
            var seed = new RunSeed(PlayedSeed);
            Purpose[] purposes =
            [
                Purpose.UnknownRoom, Purpose.PotionChance, Purpose.Gold, Purpose.Potion(), Purpose.Potion(1), Purpose.Card(0, 0), Purpose.Card(0, 1), Purpose.Card(1, 0),
                Purpose.RelicRarity(), Purpose.ChestGold, Purpose.ChestRarity, Purpose.ShopSale, Purpose.ShopCard(0), Purpose.ShopCard(1), Purpose.ShopPrice(0),
                Purpose.ShopPotion(0), Purpose.ShopRelicRarity(0), Purpose.Transform(), Purpose.Relic("WarPaint"), Purpose.Relic("Whetstone"),
                Purpose.Fight(RunStream.Shuffle), Purpose.Fight(RunStream.MonsterAi),
            ];
            var seen = new HashSet<ulong>();
            var count = 0;
            for (var act = 0; act < 3; act++)
            for (var row = 0; row <= 16; row++)
            for (var col = 0; col < 7; col++)
            for (var depth = 0; depth < 2; depth++)
                foreach (var purpose in purposes)
                {
                    seen.Add(RunStreams.KeyedSeed(seed, new RunPlace(act, row, col, depth), purpose));
                    count++;
                }
            c.Equal(count, seen.Count, "different seeds for all of 3 acts x 17 rows x 7 columns x 2 depths x 22 purposes");
            c.That(RunStreams.KeyedSeed(seed, new RunPlace(0, 7, 1), Purpose.Gold) != RunStreams.KeyedSeed(new RunSeed("KMEHXZW0R5"), new RunPlace(0, 7, 1), Purpose.Gold), "another run seed, another stream");

            var streams = new RunStreams(seed, StreamMode.Keyed);
            streams.MoveTo(1, 4, 6);
            c.Equal(new RunPlace(1, 4, 6, 0), streams.Place, "the place after moving");
            var first = streams.Draw(RunStream.Niche, Purpose.Relic("WarPaint")).NextU64();
            var again = streams.Draw(RunStream.Niche, Purpose.Relic("WarPaint")).NextU64();
            c.That(first != again, "the same purpose asked twice at one place gives two different rolls");
            c.Equal(first, new GameRng(RunStreams.KeyedSeed(seed, new RunPlace(1, 4, 6), Purpose.Relic("WarPaint"), 0)).NextU64(), "the first is the stream of that place and purpose");
            c.Equal(again, new GameRng(RunStreams.KeyedSeed(seed, new RunPlace(1, 4, 6), Purpose.Relic("WarPaint"), 1)).NextU64(), "the second is its second stream");

            streams.PushRoom();
            c.Equal(1, streams.Place.Depth, "a room on top of the node's room");
            var inFight = streams.Draw(RunStream.Rewards, Purpose.Gold).NextU64();
            streams.PopRoom();
            c.Equal(0, streams.Place.Depth, "and gone again");
            c.That(inFight != streams.Draw(RunStream.Rewards, Purpose.Gold).NextU64(), "the room on top rolls its own gold");

            // A copy carries the place and how often each purpose was asked.
            var copy = streams.Clone();
            c.Equal(streams.Draw(RunStream.Rewards, Purpose.Gold).NextU64(), copy.Draw(RunStream.Rewards, Purpose.Gold).NextU64(), "a copy goes on exactly as the original");
            streams.MoveTo(1, 4, 6);
            c.Equal(first, streams.Draw(RunStream.Niche, Purpose.Relic("WarPaint")).NextU64(), "entering a node starts its count again");
        });
    }

    private static void FightStreamsCheck(Check c)
    {
        c.Group("a fight borrows the run's streams and gives them back (game mode)", () =>
        {
            var streams = new RunStreams(PlayedSeed);
            streams.Long(RunStream.Shuffle).NextU64();
            var fight = streams.LendToFight("ShrinkerBeetleWeak", totalFloor: 2);
            c.Equal(1, fight.Shuffle.Counter, "the fight's shuffle stream stands where the run's stands");
            c.That(!ReferenceEquals(fight.Shuffle, streams.Long(RunStream.Shuffle)), "it is a copy: a fight copies itself before every action");
            c.Equal(9602888757974343678UL, fight.Encounter.NextU64(), "the encounter's own stream is the game's for that floor");
            for (var i = 0; i < 5; i++) fight.Shuffle.NextU64();
            fight.MonsterAi.NextU64();
            fight.Niche.NextU64();
            c.Equal(1, streams.Counter(RunStream.Shuffle), "the run's stream waits while the fight runs");
            var next = fight.Shuffle.Clone().NextU64();
            streams.TakeBack(fight);
            c.Equal(6, streams.Counter(RunStream.Shuffle), "after the fight the run's shuffle stream has given what the fight took");
            c.Equal(1, streams.Counter(RunStream.MonsterAi), "monster_ai too");
            c.Equal(1, streams.Counter(RunStream.Niche), "and niche");
            c.Equal(0, streams.Counter(RunStream.CombatTargets), "a stream the fight did not use stands where it stood");
            c.Equal(next, streams.LendToFight("FuzzyWurmCrawlerWeak", 3).Shuffle.NextU64(), "the next fight goes on where this one stopped");
        });

        c.Group("in keyed mode a fight starts its streams afresh", () =>
        {
            var streams = new RunStreams(PlayedSeed, StreamMode.Keyed);
            streams.MoveTo(0, 1, 5);
            var fight = streams.LendToFight("ShrinkerBeetleWeak", 2);
            c.Equal(0, fight.Shuffle.Counter, "a fresh shuffle stream");
            var first = fight.Shuffle.NextU64();
            c.That(first != fight.MonsterAi.NextU64(), "each of the fight's streams is its own");
            c.Equal(9602888757974343678UL, fight.Encounter.NextU64(), "the encounter's stream is the game's in both modes");
            streams.TakeBack(fight);
            c.Equal(0, streams.Counter(RunStream.Shuffle), "nothing goes back into the long streams");

            var other = new RunStreams(PlayedSeed, StreamMode.Keyed);
            other.MoveTo(0, 2, 5);
            other.Draw(RunStream.Rewards, Purpose.Gold).NextU64();
            other.MoveTo(0, 1, 5);
            c.Equal(first, other.LendToFight("ShrinkerBeetleWeak", 2).Shuffle.NextU64(), "the same fight on the same node shuffles the same for every player");
        });
    }

    // ---------- the odds ----------

    /// <summary>A stream whose next number, read as a float, lies in [low, high).</summary>
    private static GameRng NextBetween(float low, float high)
    {
        for (ulong seed = 1; seed < 1_000_000; seed++)
        {
            var roll = new GameRng(seed).NextFloat();
            if (roll >= low && roll < high) return new GameRng(seed);
        }
        throw new InvalidOperationException($"no stream starts with a number in [{low}, {high})");
    }

    private static void RarityOdds(Check c)
    {
        c.Group("card rarity: the thresholds of each table", () =>
        {
            // Regular, at the start of a run: Rare below 0.03 - 0.05 (never), Uncommon below 0.37 + 0.03 - 0.05 = 0.35.
            c.Equal(CardRarity.Uncommon, new CardRarityOdds().Roll(NextBetween(0.34f, 0.3499f), CardOddsType.Regular), "regular, offset -0.05, roll 0.34");
            c.Equal(CardRarity.Common, new CardRarityOdds().Roll(NextBetween(0.3501f, 0.36f), CardOddsType.Regular), "regular, offset -0.05, roll 0.355");
            c.Equal(CardRarity.Uncommon, new CardRarityOdds().Roll(NextBetween(0f, 0.001f), CardOddsType.Regular), "regular, offset -0.05, roll 0.0005: no Rare at the start");
            // Regular with the offset at 0: Rare below 0.03, Uncommon below 0.40.
            c.Equal(CardRarity.Rare, new CardRarityOdds(0f).Roll(NextBetween(0.02f, 0.0299f), CardOddsType.Regular), "regular, offset 0, roll 0.025");
            c.Equal(CardRarity.Uncommon, new CardRarityOdds(0f).Roll(NextBetween(0.0301f, 0.04f), CardOddsType.Regular), "regular, offset 0, roll 0.035");
            c.Equal(CardRarity.Uncommon, new CardRarityOdds(0f).Roll(NextBetween(0.39f, 0.3999f), CardOddsType.Regular), "regular, offset 0, roll 0.395");
            c.Equal(CardRarity.Common, new CardRarityOdds(0f).Roll(NextBetween(0.4001f, 0.41f), CardOddsType.Regular), "regular, offset 0, roll 0.405");
            // Elite with the offset at 0: Rare below 0.10, Uncommon below 0.50.
            c.Equal(CardRarity.Rare, new CardRarityOdds(0f).Roll(NextBetween(0.09f, 0.0999f), CardOddsType.Elite), "elite, offset 0, roll 0.095");
            c.Equal(CardRarity.Uncommon, new CardRarityOdds(0f).Roll(NextBetween(0.49f, 0.4999f), CardOddsType.Elite), "elite, offset 0, roll 0.495");
            c.Equal(CardRarity.Common, new CardRarityOdds(0f).Roll(NextBetween(0.5001f, 0.51f), CardOddsType.Elite), "elite, offset 0, roll 0.505");
            // Shop with the offset at 0: Rare below 0.09, Uncommon below 0.46.
            c.Equal(CardRarity.Rare, new CardRarityOdds(0f).RollWithoutChangingFutureOdds(NextBetween(0.08f, 0.0899f), CardOddsType.Shop), "shop, offset 0, roll 0.085");
            c.Equal(CardRarity.Uncommon, new CardRarityOdds(0f).RollWithoutChangingFutureOdds(NextBetween(0.45f, 0.4599f), CardOddsType.Shop), "shop, offset 0, roll 0.455");
            c.Equal(CardRarity.Common, new CardRarityOdds(0f).RollWithoutChangingFutureOdds(NextBetween(0.4601f, 0.47f), CardOddsType.Shop), "shop, offset 0, roll 0.465");
            // Uniform with the offset at 0: Rare below 0.33, Uncommon below 0.66.
            c.Equal(CardRarity.Rare, new CardRarityOdds(0f).Roll(NextBetween(0.32f, 0.3299f), CardOddsType.Uniform), "uniform, roll 0.325");
            c.Equal(CardRarity.Uncommon, new CardRarityOdds(0f).Roll(NextBetween(0.65f, 0.6599f), CardOddsType.Uniform), "uniform, roll 0.655");
            c.Equal(CardRarity.Common, new CardRarityOdds(0f).Roll(NextBetween(0.6601f, 0.67f), CardOddsType.Uniform), "uniform, roll 0.665");
        });

        c.Group("card rarity: the pity offset, in 32-bit floats", () =>
        {
            var odds = new CardRarityOdds();
            c.Equal(-0.05f, odds.Offset, "a run starts at -0.05");
            // Six cards that are not Rare. A save of the real game shows -0.02 after three and 0.01 after six.
            float[] wanted = [-0.04f, -0.03f, -0.02f, -0.01f, 0f, 0.01f];
            for (var i = 0; i < wanted.Length; i++)
            {
                c.Equal(CardRarity.Common, odds.Roll(NextBetween(0.9f, 0.95f), CardOddsType.Regular), $"card {i + 1} is Common");
                c.Equal(wanted[i], odds.Offset, $"the offset after {i + 1} cards");
            }
            // With the offset at 0.01, Rare is below 0.04.
            c.Equal(CardRarity.Rare, odds.Roll(NextBetween(0.031f, 0.039f), CardOddsType.Regular), "roll 0.035 at offset 0.01 is Rare");
            c.Equal(-0.05f, odds.Offset, "a Rare puts the offset back to -0.05");

            var high = new CardRarityOdds(0.395f);
            high.Roll(NextBetween(0.99f, 1f), CardOddsType.Regular);
            c.Equal(0.4f, high.Offset, "the offset stops at 0.4");

            // The shop's roll and the roll with a named offset leave the run's offset alone.
            var shop = new CardRarityOdds(0.2f);
            shop.RollWithoutChangingFutureOdds(NextBetween(0.9f, 0.95f), CardOddsType.Shop);
            c.Equal(0.2f, shop.Offset, "a shop card does not move the offset");
            c.Equal(CardRarity.Rare, shop.RollWithoutChangingFutureOdds(NextBetween(0.28f, 0.2899f), CardOddsType.Shop), "but the offset counts in a shop: Rare below 0.09 + 0.2");
            c.Equal(CardRarity.Common, CardRarityOdds.RollWithOffset(NextBetween(0.9f, 0.95f), CardOddsType.Regular, 0.3f), "a roll with a named offset");

            var copy = odds.Clone();
            copy.Roll(NextBetween(0.9f, 0.95f), CardOddsType.Regular);
            c.Equal(-0.05f, odds.Offset, "a copy moves alone");
        });

        c.Group("card rarity: a boss card is always Rare and leaves the offset out of the roll", () =>
        {
            var odds = new CardRarityOdds(0.2f);
            foreach (var (low, high) in new[] { (0f, 0.01f), (0.5f, 0.51f), (0.98f, 0.99f) })
                c.Equal(CardRarity.Rare, odds.Roll(NextBetween(low, high), CardOddsType.Boss), $"boss card, roll {low}");
            c.Equal(-0.05f, odds.Offset, "and the offset is back at its start");
            var rng = NextBetween(0.5f, 0.51f);
            new CardRarityOdds().Roll(rng, CardOddsType.Boss);
            c.Equal(1, rng.Counter, "one number per roll, also when the answer is certain");
        });

        c.Group("card rarity without an offset: Uncommon is tested against its own odds alone", () =>
        {
            // Regular table: Rare below 0.03, Uncommon below 0.37 (not 0.40), else Common.
            c.Equal(CardRarity.Rare, CardRarityOdds.RollWithBaseOdds(NextBetween(0.02f, 0.0299f), CardOddsType.Regular), "roll 0.025");
            c.Equal(CardRarity.Uncommon, CardRarityOdds.RollWithBaseOdds(NextBetween(0.36f, 0.3699f), CardOddsType.Regular), "roll 0.365");
            c.Equal(CardRarity.Common, CardRarityOdds.RollWithBaseOdds(NextBetween(0.3701f, 0.38f), CardOddsType.Regular), "roll 0.375 is Common here, Uncommon in a card reward");
            c.Equal(CardRarity.Uncommon, new CardRarityOdds(0f).Roll(NextBetween(0.3701f, 0.38f), CardOddsType.Regular), "(the card reward's roll for the same number)");
            // Uniform table: both tests are against 0.33, so Uncommon never comes.
            c.Equal(CardRarity.Rare, CardRarityOdds.RollWithBaseOdds(NextBetween(0.32f, 0.3299f), CardOddsType.Uniform), "uniform, roll 0.325");
            c.Equal(CardRarity.Common, CardRarityOdds.RollWithBaseOdds(NextBetween(0.3301f, 0.34f), CardOddsType.Uniform), "uniform, roll 0.335");
        });
    }

    private static void PotionAndUnknownOdds(Check c)
    {
        c.Group("potion drops: 0.4 to start, down 0.1 after a drop, up 0.1 after none, an elite adds 0.125", () =>
        {
            var odds = new PotionOdds();
            c.Equal(0.4f, odds.Chance, "a run starts at 0.4");
            c.That(!odds.Roll(NextBetween(0.4001f, 0.41f), elite: false), "roll 0.405 against 0.4: no drop");
            c.Equal(0.5f, odds.Chance, "the chance after no drop");
            c.That(odds.Roll(NextBetween(0.49f, 0.4999f), elite: false), "roll 0.495 against 0.5: a drop");
            c.Equal(0.4f, odds.Chance, "the chance after a drop");
            c.That(odds.Roll(NextBetween(0.39f, 0.3999f), elite: false), "roll 0.395 against 0.4: a drop");
            c.Equal(0.3f, odds.Chance, "0.4 minus 0.1 in floats is the float 0.3");

            c.That(new PotionOdds().Roll(NextBetween(0.52f, 0.5249f), elite: true), "an elite: roll 0.522 against 0.4 + 0.125 is a drop");
            c.That(!new PotionOdds().Roll(NextBetween(0.5251f, 0.53f), elite: true), "an elite: roll 0.527 is none");
            c.That(!new PotionOdds().Roll(NextBetween(0.52f, 0.5249f), elite: false), "the same roll in a hallway or boss fight is none");

            var forced = new PotionOdds();
            var rng = NextBetween(0.9f, 0.95f);
            c.That(forced.Roll(rng, elite: false, forced: true), "a forced drop drops whatever the roll");
            c.Equal(1, rng.Counter, "the number is drawn all the same");
            c.Equal(0.3f, forced.Chance, "and the chance goes down");
        });

        c.Group("Unknown rooms: the roll walks Monster, Treasure, Shop; the rest is an Event", () =>
        {
            var all = UnknownRoomOdds.AllRooms;
            // Start: Monster to 0.10, Treasure to 0.12, Shop to 0.15.
            c.Equal(RolledRoom.Monster, new UnknownRoomOdds().Roll(NextBetween(0.09f, 0.0999f), all), "roll 0.095");
            c.Equal(RolledRoom.Treasure, new UnknownRoomOdds().Roll(NextBetween(0.1001f, 0.1199f), all), "roll 0.11");
            c.Equal(RolledRoom.Shop, new UnknownRoomOdds().Roll(NextBetween(0.1201f, 0.1499f), all), "roll 0.13");
            c.Equal(RolledRoom.Event, new UnknownRoomOdds().Roll(NextBetween(0.1501f, 0.16f), all), "roll 0.155");

            var odds = new UnknownRoomOdds();
            odds.Roll(NextBetween(0.9f, 0.95f), all);
            // The real game's save after one Unknown room that was an Event.
            c.That(odds.Monster == 0.2f && odds.Elite == -2f && odds.Treasure == 0.04f && odds.Shop == 0.06f, "after an Event: 0.2, -2, 0.04, 0.06");
            for (var i = 0; i < 3; i++) odds.Roll(NextBetween(0.9f, 0.95f), all);
            // Four Events in a row, added up in floats: 0.02f five times is not the float 0.1, 0.03f five times not 0.15.
            c.Equal(0.5f, odds.Monster, "Monster after four Events");
            c.Equal(-5f, odds.Elite, "Elite after four Events");
            c.Equal(0.099999994f, odds.Treasure, "Treasure after four Events");
            c.Equal(0.14999999f, odds.Shop, "Shop after four Events");
            c.That(odds.Treasure != 0.1f && odds.Shop != 0.15f, "neither is the rounded decimal");

            c.Equal(RolledRoom.Treasure, odds.Roll(NextBetween(0.55f, 0.59f), all), "roll 0.57: past Monster (0.5), within Treasure (0.6)");
            c.That(odds.Monster == 0.6f && odds.Treasure == 0.02f, "the rolled kind goes back to its start, Monster grows on");
            odds.ResetToBase();
            c.That(odds.Monster == 0.1f && odds.Elite == -1f && odds.Treasure == 0.02f && odds.Shop == 0.03f, "a new act: every kind back to its start");
        });

        c.Group("Unknown rooms: a kind that is barred is not in the roll and does not grow", () =>
        {
            var noShop = UnknownRoomOdds.Allowed(shopBarred: true);
            c.That(!noShop.Contains(RolledRoom.Shop) && noShop.Count == 4, "a barred Shop is the only kind missing");
            c.Equal(5, UnknownRoomOdds.Allowed(shopBarred: false).Count, "nothing barred: all five kinds");
            var odds = new UnknownRoomOdds();
            c.Equal(RolledRoom.Event, odds.Roll(NextBetween(0.1201f, 0.1499f), noShop), "roll 0.13 with Shop barred is an Event");
            c.Equal(0.03f, odds.Shop, "and the Shop odds stay");
            c.Equal(0.2f, odds.Monster, "the others grow");

            // Juzu Bracelet takes Monster out: the walk starts at Treasure.
            var noMonster = new HashSet<RolledRoom>(UnknownRoomOdds.AllRooms);
            noMonster.Remove(RolledRoom.Monster);
            var juzu = new UnknownRoomOdds();
            c.Equal(RolledRoom.Treasure, juzu.Roll(NextBetween(0.01f, 0.0199f), noMonster), "roll 0.015 without Monster is a Treasure room");
            c.Equal(0.1f, juzu.Monster, "Monster does not grow while it is barred");

            // With Event barred as well, the fallback is the lowest kind that is left.
            var onlyFights = new HashSet<RolledRoom> { RolledRoom.Treasure, RolledRoom.Shop };
            c.Equal(RolledRoom.Treasure, new UnknownRoomOdds().Roll(NextBetween(0.9f, 0.95f), onlyFights), "no Event allowed and a high roll: the lowest kind left");

            var copy = odds.Clone();
            copy.Roll(NextBetween(0.9f, 0.95f), UnknownRoomOdds.AllRooms);
            c.Equal(0.2f, odds.Monster, "a copy moves alone");
        });
    }

    // ---------- one real run, room by room ----------

    private static void RealRunRolls(Check c)
    {
        c.Group("a real run's rolls (seed 1BUSLN0FY5): the gold, the potion drops and the card rarities of its first two fights", () =>
        {
            // What the game recorded for this seed: fight 1 gave 11 gold, no potion, and offered Colossus,
            // Unrelenting, Havoc; fight 2 gave 10 gold, a potion, and offered Fight Me!, Stone Armor,
            // Setup Strike. Its saves show the rewards stream at 1 before the first fight, 12 after it
            // and 25 after the second; the pity offset at -0.02 and 0.01; the potion chance at 0.5 and 0.4.
            var registry = Content.Registry;
            CardRarity Rarity(string card) => registry.Card(card).Rarity;

            var streams = new RunStreams(PlayedSeed);
            streams.FastForward(RunStream.Rewards, 1);
            var rarity = new CardRarityOdds();
            var potion = new PotionOdds();

            // The game's order after a fight: the potion roll, the gold, the potion itself (rarity and
            // pick) when one dropped, then per card the rarity, the pick and the upgrade roll.
            string[][] offered = [["Colossus", "Unrelenting", "Havoc"], ["FightMe", "StoneArmor", "SetupStrike"]];
            bool[] dropped = [false, true];
            int[] gold = [11, 10];
            float[] chance = [0.5f, 0.4f];
            float[] offset = [-0.02f, 0.01f];
            int[] counter = [12, 25];
            for (var fight = 0; fight < 2; fight++)
            {
                c.Equal(dropped[fight], potion.Roll(streams.Draw(RunStream.Rewards, Purpose.PotionChance), elite: false), $"fight {fight + 1}: a potion dropped");
                c.Equal(gold[fight], streams.Draw(RunStream.Rewards, Purpose.Gold).NextInt(10, 21), $"fight {fight + 1}: gold, one of 10 to 20");
                if (dropped[fight])
                {
                    var which = streams.Draw(RunStream.Rewards, Purpose.Potion());
                    which.NextFloat();
                    which.NextU64();
                }
                for (var card = 0; card < 3; card++)
                {
                    var rng = streams.Draw(RunStream.Rewards, Purpose.Card(0, card));
                    c.Equal(Rarity(offered[fight][card]), rarity.Roll(rng, CardOddsType.Regular), $"fight {fight + 1}: the rarity rolled for card {card + 1}, which was {offered[fight][card]}");
                    rng.NextU64();
                    rng.NextU64();
                }
                c.Equal(chance[fight], potion.Chance, $"the potion chance after fight {fight + 1}");
                c.Equal(offset[fight], rarity.Offset, $"the pity offset after fight {fight + 1}");
                c.Equal(counter[fight], streams.Counter(RunStream.Rewards), $"the rewards counter after fight {fight + 1}");
            }

            // Then the event This or That gave a relic, and the save after it shows rewards at 26: one number
            // for the relic's rarity (Common below 0.5, Uncommon below 0.83, else Rare). The relic was Stone
            // Cracker, which is the front of the player's Uncommon list for this seed.
            var relicRoll = streams.Draw(RunStream.Rewards, Purpose.RelicRarity()).NextFloat();
            c.That(relicRoll >= 0.5f && relicRoll < 0.83f, FormattableString.Invariant($"the 26th number of rewards rolls an Uncommon relic (it is {relicRoll})"));
            var bag = RunGeneration.Start(new RunStreams(PlayedSeed), RunUnlocks.All).PlayerBag;
            c.Equal("StoneCracker", bag.PullFromFront(RelicRarity.Uncommon), "and the front of the Uncommon list is the relic the run got");
        });

        c.Group("a real run's rolls (seed 1BUSLN0FY5): its first Unknown room was an Event", () =>
        {
            var streams = new RunStreams(PlayedSeed);
            var odds = new UnknownRoomOdds();
            c.Equal(RolledRoom.Event, odds.Roll(streams.Draw(RunStream.UnknownMapPoint, Purpose.UnknownRoom), UnknownRoomOdds.Allowed(shopBarred: false)), "the room (the game's record: the event This or That)");
            c.Equal(1, streams.Counter(RunStream.UnknownMapPoint), "one number, as the save's counter says");
            c.That(odds.Monster == 0.2f && odds.Elite == -2f && odds.Treasure == 0.04f && odds.Shop == 0.06f, "the odds the save then holds: 0.2, -2, 0.04, 0.06");
        });

        c.Group("a real run's relics (seed 1BUSLN0FY5) stand where the bag has them", () =>
        {
            var start = RunGeneration.Start(new RunStreams(PlayedSeed), RunUnlocks.All);
            var bag = start.PlayerBag;
            // This or That gave Stone Cracker: the front of the Uncommon list.
            c.Equal("StoneCracker", bag.Relics(RelicRarity.Uncommon)[0], "front of the player's Uncommon list");
            // The run's first shop showed Paper Phrog, Pendulum and Ringing Triangle. Shops take from the back
            // and never sell Bowler Hat or Lucky Fysh, which stand behind Paper Phrog.
            c.Sequence(["PaperPhrog", "LuckyFysh", "BowlerHat"], bag.Relics(RelicRarity.Uncommon).TakeLast(3), "back of the Uncommon list");
            c.Equal("Pendulum", bag.Relics(RelicRarity.Common)[^1], "back of the Common list");
            c.Equal("RingingTriangle", bag.Relics(RelicRarity.Shop)[^1], "back of the Shop list");
            // The run's first three fights.
            c.Sequence(["ShrinkerBeetleWeak", "FuzzyWurmCrawlerWeak", "SlimesWeak"], start.Rooms[0].NormalEncounters.Take(3), "the three weak fights, in the order the run met them");
            c.Equal("ThisOrThat", start.Rooms[0].Events[1], "This or That is the second event of the list");

            // Why the run's first Event room took entry 1 and not entry 0 (The Future of Potions): the game
            // moves the event counter for every room of type Event that is entered as a node's own room,
            // and Neow's room is one. The save taken after Neow's pick (run-164521, not embedded) has
            // events_visited at 1 with no Event room met yet; the save three rooms later has 2.
            var rooms = start.Rooms[0];
            c.Equal("TheFutureOfPotions", rooms.NextEvent, "before Neow the counter stands on entry 0");
            rooms.MarkEventVisited();
            c.Equal("ThisOrThat", rooms.NextEvent, "after Neow's room it stands on entry 1: the event the run then met");
            rooms.MarkEventVisited();
            var later = GoldenSave.Parse(RunGoldenData.Saves.Single(s => s.File.StartsWith("later-", StringComparison.Ordinal)).Json);
            var savedRooms = later.Root.GetProperty("acts")[0].GetProperty("rooms");
            c.Equal(savedRooms.GetProperty("events_visited").GetInt32(), rooms.EventsVisited, "the event counter after Neow and one Event room, as the later save has it (2)");
            c.Equal(2, savedRooms.GetProperty("events_visited").GetInt32(), "which is 2");
            c.Equal(2, savedRooms.GetProperty("normal_encounters_visited").GetInt32(), "and the hallway counter is 2 after two fights");
        });
    }

    // ---------- the relic bags ----------

    private static void Bags(Check c)
    {
        c.Group("filling the bags: one shuffle per rarity, in the order the rarities first appear", () =>
        {
            var rng = new RunStreams(PlayedSeed).Long(RunStream.UpFront);
            var shared = new RelicBag(refreshAllowed: true);
            shared.Populate(RunUnlocks.All.Relics(GameLists.SharedRelics), rng);
            // A shuffle of n relics takes n - 1 numbers: 29 + 24 + 34 + 24 + 0 + 1.
            c.Equal(112, rng.Counter, "numbers for the shared bag");
            c.Sequence([RelicRarity.Uncommon, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Shop, RelicRarity.Event, RelicRarity.Ancient], shared.Rarities, "its lists");
            c.Equal(118, shared.Rarities.Sum(r => shared.Relics(r).Count), "it holds the whole shared pool");

            var player = new RelicBag();
            player.Populate(RelicBag.PlayerPool(RunUnlocks.All, "Ironclad"), rng);
            // 31 + 25 + 37 + 25.
            c.Equal(112 + 118, rng.Counter, "numbers for the player's bag");
            c.Sequence([RelicRarity.Uncommon, RelicRarity.Common, RelicRarity.Rare, RelicRarity.Shop], player.Rarities, "its lists");
            c.That(!player.Contains("BurningBlood") && !player.Contains("FresnelLens") && player.Contains("Brimstone"), "no starter and no Event relic in it; the Ironclad's own are");
            c.That(shared.Contains("FresnelLens") && !shared.Contains("Brimstone"), "the shared bag has the Event relic and no character's");

            var threw = false;
            try { player.Populate(["Anchor"], rng); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a bag is filled once");
        });

        c.Group("pulling: front, back, a filter, and a relic that is pulled is gone", () =>
        {
            var bag = new RelicBag();
            bag.Populate(["Anchor", "Vajra", "Lantern", "Strawberry", "Akabeko", "Pear", "Mango"], new GameRng(11));
            var common = bag.Relics(RelicRarity.Common).ToList();
            c.Equal(4, common.Count, "four Common relics");
            c.Equal(common[0], bag.PullFromFront(RelicRarity.Common), "the front");
            c.Equal(common[3], bag.PullFromBack(RelicRarity.Common), "the back");
            c.Sequence([common[1], common[2]], bag.Relics(RelicRarity.Common), "what is left keeps its order");
            c.Equal(common[2], bag.PullFromFront(RelicRarity.Common, filter: r => r == common[2]), "a filter passes over the front");
            c.Equal(common[1], bag.PullFromBack(RelicRarity.Common), "the last one");
            c.That(!bag.Contains(common[0]), "a pulled relic is not in the bag any more");

            bag.Remove("Mango");
            c.Equal(0, bag.Relics(RelicRarity.Rare).Count, "a relic taken out by name");
            bag.Remove("Mango");
            c.Equal(2, bag.Relics(RelicRarity.Uncommon).Count, "taking it out again does nothing");
        });

        c.Group("an empty rarity gives way: Shop, Common, Uncommon, Rare, and after Rare nothing", () =>
        {
            RelicBag Make()
            {
                var bag = new RelicBag();
                bag.Populate(["Akabeko", "Mango", "Bread"], new GameRng(3));
                return bag;
            }
            c.Equal("Akabeko", Make().PullFromFront(RelicRarity.Common), "no Common: the Uncommon one");
            c.Equal("Akabeko", Make().PullFromBack(RelicRarity.Shop, filter: r => r != "Bread"), "the Shop one filtered out: past Common to Uncommon");
            c.Equal("Mango", Make().PullFromFront(RelicRarity.Uncommon, filter: r => r != "Akabeko"), "the Uncommon one filtered out: the Rare one");
            var bag = Make();
            c.Equal("Mango", bag.PullFromFront(RelicRarity.Rare), "the Rare one");
            c.Equal(null, bag.PullFromFront(RelicRarity.Rare), "Rare never gives way to Shop: nothing, though Bread is still there");
            c.Equal(null, bag.PullFromFront(RelicRarity.Event), "an Event-rarity pull finds nothing");
            c.That(bag.HasAvailableRelics(), "something is still left for a pull of the bag rarities");
            bag.PullFromFront(RelicRarity.Shop);
            bag.PullFromFront(RelicRarity.Shop);
            c.That(!bag.HasAvailableRelics(), "until the bag is empty");
        });

        c.Group("a relic the run no longer allows is dropped for good at the next pull", () =>
        {
            var bag = new RelicBag();
            bag.Populate(["Anchor", "Vajra", "Akabeko"], new GameRng(3));
            var pulled = bag.PullFromFront(RelicRarity.Uncommon, isAllowed: r => r != "Vajra");
            c.Equal("Akabeko", pulled, "the pull itself");
            c.That(!bag.Contains("Vajra") && bag.Contains("Anchor"), "Vajra is gone from its own list, though the pull was of another rarity");
        });

        c.Group("the shared bag fills an empty rarity again, in the pool's order; the player's bag does not", () =>
        {
            string[] relics = ["Anchor", "Vajra", "Lantern", "Akabeko"];
            var shared = new RelicBag(refreshAllowed: true);
            shared.Populate(relics, new GameRng(8));
            for (var i = 0; i < 3; i++) shared.PullFromFront(RelicRarity.Common);
            c.Equal(0, shared.Relics(RelicRarity.Common).Count, "Common is empty");
            c.Equal("Anchor", shared.PullFromFront(RelicRarity.Common), "the next pull meets the refilled list, unshuffled");
            c.Sequence(["Vajra", "Lantern"], shared.Relics(RelicRarity.Common), "the rest of the refill");

            var player = new RelicBag();
            player.Populate(relics, new GameRng(8));
            for (var i = 0; i < 3; i++) player.PullFromFront(RelicRarity.Common);
            c.Equal("Akabeko", player.PullFromFront(RelicRarity.Common), "the player's bag gives way to Uncommon instead");

            // A rarity the bag never had a list for: the game's refill makes no list either, and the pull gives way.
            var noShop = new RelicBag(refreshAllowed: true);
            noShop.Populate(relics, new GameRng(8));
            var front = noShop.Relics(RelicRarity.Common)[0];
            c.Equal(front, noShop.PullFromFront(RelicRarity.Shop), "a Shop pull from a bag without Shop relics takes a Common one");

            var copy = shared.Clone();
            copy.PullFromFront(RelicRarity.Common);
            c.Equal(2, shared.Relics(RelicRarity.Common).Count, "a copy of a bag pulls for itself");
        });
    }

    // ---------- the act roll ----------

    private static void ActRoll(Check c)
    {
        c.Group("the first act is rolled on act_selection: Overgrowth or Underdocks, half and half with everything unlocked", () =>
        {
            // The five real saves: four Overgrowth runs and one Underdocks run.
            c.Sequence(["Overgrowth", "Hive", "Glory"], RunGeneration.RollActs(new RunSeed("KMEHXZW0R5"), RunUnlocks.All), "KMEHXZW0R5");
            c.Sequence(["Underdocks", "Hive", "Glory"], RunGeneration.RollActs(new RunSeed("F30CH975SF"), RunUnlocks.All), "F30CH975SF");

            var rng = new GameRng(2026);
            const int runs = 4000;
            var overgrowth = 0;
            for (var i = 0; i < runs; i++)
                if (RunGeneration.RollActs(RunSeed.Random(rng), RunUnlocks.All)[0] == "Overgrowth") overgrowth++;
            // Three standard deviations of a fair coin over 4000 throws are 95.
            c.That(Math.Abs(overgrowth - runs / 2) < 120, string.Create(CultureInfo.InvariantCulture, $"Overgrowth in {overgrowth} of {runs} seeds"));
        });

        c.Group("the first act of a profile without Underdocks, and of one that has just unlocked it", () =>
        {
            var encounters = GameLists.Acts.SelectMany(a => a.Encounters).Select(e => e.ClassName).ToList();
            var locked = new RunUnlocks(GameLists.Epochs.Where(e => e != "UNDERDOCKS_EPOCH"), encounters, 50, ["Overgrowth", "Hive", "Glory"]);
            var fresh = new RunUnlocks(GameLists.Epochs, encounters, 50, ["Overgrowth", "Hive", "Glory"]);
            var rng = new GameRng(7);
            for (var i = 0; i < 200; i++)
            {
                var seed = RunSeed.Random(rng);
                if (RunGeneration.RollActs(seed, locked)[0] != "Overgrowth") c.That(false, $"{seed}: Underdocks without its epoch");
                if (RunGeneration.RollActs(seed, fresh)[0] != "Underdocks") c.That(false, $"{seed}: an unlocked act never entered must be taken");
            }
            c.That(true, "always Overgrowth without the epoch; always Underdocks the first time after it");
        });
    }

    // ---------- the real saves ----------

    private static void GoldenSaves(Check c)
    {
        // rng.counters.up_front of each save.
        (string Seed, int UpFront)[] counts = [("KMEHXZW0R5", 411), ("EEXRY5WA1R", 413), ("F30CH975SF", 406), ("E9U400ERJ4", 417), ("1BUSLN0FY5", 419), ("1BUSLN0FY5", 419)];

        for (var i = 0; i < RunGoldenData.Saves.Count; i++)
        {
            var data = RunGoldenData.Saves[i];
            var (seed, upFront) = counts[i];
            c.Group($"real save {data.File} (seed {seed}) is rebuilt from its seed and unlock state", () =>
            {
                var save = GoldenSave.Parse(data.Json);
                c.Equal(seed, save.Seed.Text, "the seed");
                c.Equal("Ironclad", save.Character, "the character");
                c.Equal(upFront, save.Counters["up_front"], "the save's up_front count");
                c.Sequence([], save.Compare(), "differences between the save and what the seed gives");

                // The same once more with nothing between the save and the result but the generator.
                var start = RunGeneration.Start(new RunStreams(save.Seed), save.Unlocks);
                c.Equal(upFront, start.UpFrontDraws, "numbers taken from up_front");
                c.Sequence(save.Acts.Select(a => a.Act), start.Acts, "the acts");
                for (var act = 0; act < 3; act++)
                {
                    c.Sequence(save.Acts[act].Events, start.Rooms[act].Events.Select(GameNames.Slug), $"act {act + 1} events");
                    c.Sequence(save.Acts[act].NormalEncounters, start.Rooms[act].NormalEncounters.Select(GameNames.Slug), $"act {act + 1} hallway fights");
                    c.Sequence(save.Acts[act].EliteEncounters, start.Rooms[act].EliteEncounters.Select(GameNames.Slug), $"act {act + 1} elites");
                    c.Equal(save.Acts[act].Boss, GameNames.Slug(start.Rooms[act].Boss), $"act {act + 1} boss");
                    c.Equal(save.Acts[act].Ancient, GameNames.Slug(start.Rooms[act].Ancient ?? ""), $"act {act + 1} Ancient");
                }
            });
        }

        c.Group("the saves taken on Neow's screen hold both bags whole and in the generated order", () =>
        {
            foreach (var data in RunGoldenData.Saves.Take(5))
            {
                var save = GoldenSave.Parse(data.Json);
                var start = RunGeneration.Start(new RunStreams(save.Seed), save.Unlocks);
                c.Equal(0, save.RelicsGoneFromBags(), $"{data.File}: relics gone from the bags");
                foreach (var (rarity, relics) in save.SharedBag)
                    c.Sequence(relics, start.SharedBag.Relics(rarity).Select(GameNames.Slug), $"{data.File}: shared bag, {rarity}");
                foreach (var (rarity, relics) in save.PlayerBag)
                    c.Sequence(relics, start.PlayerBag.Relics(rarity).Select(GameNames.Slug), $"{data.File}: player bag, {rarity}");
                c.Equal(118, save.SharedBag.Sum(l => l.Relics.Count), $"{data.File}: the shared bag holds 118 relics");
                c.Equal(122, save.PlayerBag.Sum(l => l.Relics.Count), $"{data.File}: the player's bag holds 122");
            }
        });

        c.Group("a few of the saves' own words, spelled out", () =>
        {
            var first = RunGeneration.Start(new RunStreams("KMEHXZW0R5"), RunUnlocks.All);
            c.Equal(411, first.UpFrontDraws, "KMEHXZW0R5: up_front");
            c.Sequence(["SlimesWeak", "FuzzyWurmCrawlerWeak", "NibbitsWeak", "FogmogNormal"], first.Rooms[0].NormalEncounters.Take(4), "KMEHXZW0R5: the first four hallway fights");
            c.Sequence(["BygoneEffigyElite", "PhrogParasiteElite", "ByrdonisElite", "BygoneEffigyElite"], first.Rooms[0].EliteEncounters.Take(4), "KMEHXZW0R5: the first four elites");
            c.Sequence(["SelfHelpBook", "FakeMerchant", "MorphicGrove"], first.Rooms[0].Events.Take(3), "KMEHXZW0R5: the first three events");
            c.Sequence(["VantomBoss", "KaiserCrabBoss", "QueenBoss"], first.Rooms.Select(r => r.Boss), "KMEHXZW0R5: the three bosses");
            c.Sequence(["Neow", "Pael", "Vakuu"], first.Rooms.Select(r => r.Ancient ?? ""), "KMEHXZW0R5: the three Ancients");
            c.Equal("PenNib", first.SharedBag.Relics(RelicRarity.Uncommon)[0], "KMEHXZW0R5: front of the shared Uncommon list");
            c.Equal("TuningFork", first.PlayerBag.Relics(RelicRarity.Uncommon)[0], "KMEHXZW0R5: front of the player's Uncommon list");
            c.Sequence(["LoomingFruit", "VeryHotCocoa"], first.SharedBag.Relics(RelicRarity.Ancient), "KMEHXZW0R5: the Ancient-rarity list (shuffled with one number)");

            var docks = RunGeneration.Start(new RunStreams("F30CH975SF"), RunUnlocks.All);
            c.Equal(406, docks.UpFrontDraws, "F30CH975SF: up_front");
            c.Sequence(["Underdocks", "Hive", "Glory"], docks.Acts, "F30CH975SF: the acts");
            c.Sequence(["ToadpolesWeak", "SludgeSpinnerWeak", "SeapunkWeak", "FossilStalkerNormal"], docks.Rooms[0].NormalEncounters.Take(4), "F30CH975SF: the first four hallway fights");
            c.Sequence(["SoulFyshBoss", "KaiserCrabBoss", "TestSubjectBoss"], docks.Rooms.Select(r => r.Boss), "F30CH975SF: the three bosses");
            c.Sequence(["VeryHotCocoa", "LoomingFruit"], docks.SharedBag.Relics(RelicRarity.Ancient), "F30CH975SF: the Ancient-rarity list");
            // RunUnlocks.All and Mark's profile give the same Ironclad run: the nine epochs he lacks are other characters'.
            var mark = GoldenSave.Parse(RunGoldenData.Saves[0].Json).Unlocks;
            c.Equal(48, mark.Epochs.Count, "Mark's profile has 48 of the 57 epochs");
            c.That(GameLists.Epochs.Except(mark.Epochs).All(e => e.StartsWith("DEFECT", StringComparison.Ordinal) || e.StartsWith("NECROBINDER", StringComparison.Ordinal)
                || e.StartsWith("REGENT", StringComparison.Ordinal) || e.StartsWith("SILENT", StringComparison.Ordinal)), "the other nine belong to other characters");
            c.Equal(58, GoldenSave.Parse(RunGoldenData.Saves[4].Json).Unlocks.NumberOfRuns, "the profile's run count in the last save");
        });

        c.Group("a save taken three rooms later: the bags have lost Stone Cracker and nothing else changed", () =>
        {
            var save = GoldenSave.Parse(RunGoldenData.Saves[5].Json);
            c.Sequence([], save.Compare(), "differences");
            c.Equal(2, save.RelicsGoneFromBags(), "relics gone: Stone Cracker from the player's bag and from the shared one");
            var start = RunGeneration.Start(new RunStreams(save.Seed), save.Unlocks);
            foreach (var (rarity, relics) in save.PlayerBag)
                c.Sequence(relics, start.PlayerBag.Relics(rarity).Where(r => r != "StoneCracker").Select(GameNames.Slug), $"player bag, {rarity}");
            foreach (var (rarity, relics) in save.SharedBag)
                c.Sequence(relics, start.SharedBag.Relics(rarity).Where(r => r != "StoneCracker").Select(GameNames.Slug), $"shared bag, {rarity}");
            c.Equal(26, save.Counters["rewards"], "rewards counter");
            c.Equal(1, save.Counters["unknown_map_point"], "unknown_map_point counter");
        });

        c.Group("the comparison can fail: a save with one thing changed is not rebuilt", () =>
        {
            var json = RunGoldenData.Saves[0].Json;
            string Swap(string text, string a, string b) => text.Replace(a, "\u0001").Replace(b, a).Replace("\u0001", b);
            c.Equal(0, GoldenSave.Parse(json).Compare().Count, "the save as it is");
            c.That(GoldenSave.Parse(Swap(json, "\"EVENT.SELF_HELP_BOOK\"", "\"EVENT.FAKE_MERCHANT\"")).Compare().Count >= 1, "two events in the other order");
            c.That(GoldenSave.Parse(Swap(json, "\"RELIC.PEN_NIB\"", "\"RELIC.PETRIFIED_TOAD\"")).Compare().Count >= 1, "two relics of a bag in the other order");
            c.That(GoldenSave.Parse(json.Replace("\"up_front\":411", "\"up_front\":410")).Compare().Count == 1, "an up_front count that is one off");
            c.That(GoldenSave.Parse(json.Replace("\"KMEHXZW0R5\"", "\"KMEHXZW0R6\"")).Compare().Count > 5, "another seed");
            c.That(GoldenSave.Parse(json.Replace("\"ENCOUNTER.VANTOM_BOSS\",\"ancient_id\"", "\"ENCOUNTER.THE_KIN_BOSS\",\"ancient_id\"")).Compare().Count == 1, "another boss");
        });

        c.Group("golden saves on this machine, when there are any", () =>
        {
            // Every *.save in $ARENA_GOLDEN_SAVES, or in ~/Projects/sts2-notes/golden-saves: saves Mark made on v0.107.1.
            var folder = Environment.GetEnvironmentVariable("ARENA_GOLDEN_SAVES");
            if (string.IsNullOrEmpty(folder))
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "sts2-notes", "golden-saves");
            if (!Directory.Exists(folder))
            {
                c.That(true, "no folder of golden saves here");
                return;
            }
            foreach (var file in Directory.GetFiles(folder, "*.save").OrderBy(f => f, StringComparer.Ordinal))
            {
                var differences = GoldenSave.Load(file).Compare();
                c.That(differences.Count == 0, $"{Path.GetFileName(file)}: {string.Join("; ", differences)}");
            }
        });
    }

    // ---------- the rules of the lists, on many seeds ----------

    private static void GenerationRules(Check c)
    {
        c.Group("on 3000 seeds: what every act's lists must look like, and how many numbers they take", () =>
        {
            var rng = new GameRng(1011);
            const int runs = 3000;
            var bosses = new Dictionary<string, int>(StringComparer.Ordinal);
            var darvInHive = 0;
            var darvInGlory = 0;
            var minDraws = int.MaxValue;
            var problems = new List<string>();
            for (var n = 0; n < runs; n++)
            {
                var seed = RunSeed.Random(rng);
                var start = RunGeneration.Generate(new RunStreams(seed), ["Overgrowth", "Hive", "Glory"], RunUnlocks.All);
                minDraws = Math.Min(minDraws, start.UpFrontDraws);
                for (var i = 0; i < 3; i++)
                {
                    var act = GameLists.Act(start.Acts[i]);
                    var rooms = start.Rooms[i];
                    void Bad(string what) => problems.Add($"{seed} {act.ClassName}: {what}");
                    if (rooms.Events.Count != act.Events.Count + 18 || rooms.Events.Distinct().Count() != rooms.Events.Count) Bad("the events are not the act's and the 18 shared ones, each once");
                    if (rooms.NormalEncounters.Count != act.Rooms) Bad($"{rooms.NormalEncounters.Count} hallway fights");
                    var weak = rooms.NormalEncounters.Take(act.WeakEncounters).ToList();
                    if (weak.Distinct().Count() != weak.Count || weak.Any(w => act.Weak.All(e => e.ClassName != w))) Bad("the list does not start with different weak encounters");
                    var regular = rooms.NormalEncounters.Skip(act.WeakEncounters).ToList();
                    var pool = act.Regular.Select(e => e.ClassName).ToList();
                    // A bag is emptied before it is filled again: the first fights of the regular part are all different.
                    var firstBag = regular.Take(pool.Count).ToList();
                    if (firstBag.Distinct().Count() != firstBag.Count || firstBag.Any(r => !pool.Contains(r))) Bad("the regular fights repeat before the bag is empty");
                    if (rooms.EliteEncounters.Count != 15) Bad($"{rooms.EliteEncounters.Count} elites");
                    for (var block = 0; block < 5; block++)
                        if (rooms.EliteEncounters.Skip(block * 3).Take(3).Distinct().Count() != 3) Bad($"elites {block * 3 + 1} to {block * 3 + 3} are not three different ones");
                    for (var k = 1; k < 15; k++)
                        if (rooms.EliteEncounters[k] == rooms.EliteEncounters[k - 1]) Bad($"elite {k + 1} repeats elite {k}");
                    if (act.Bosses.All(b => b.ClassName != rooms.Boss)) Bad($"boss {rooms.Boss}");
                }
                bosses[start.Rooms[0].Boss] = bosses.GetValueOrDefault(start.Rooms[0].Boss) + 1;
                if (start.Rooms[0].Ancient != "Neow") problems.Add($"{seed}: act 1 opens with {start.Rooms[0].Ancient}");
                if (start.Rooms[1].Ancient == "Darv") darvInHive++;
                if (start.Rooms[2].Ancient == "Darv") darvInGlory++;
            }
            c.That(problems.Count == 0, $"{problems.Count} problems, the first: {problems.FirstOrDefault()}");
            // Both bags 230, the Ancients 2, then per act a shuffle of its events, one number at least per
            // list entry, one for the boss and one for the Ancient: 30 + 15 + 15 + 2, 27 + 14 + 15 + 2, 24 + 13 + 15 + 2.
            c.That(minDraws >= 232 + 62 + 58 + 54, $"never fewer than 406 numbers (the fewest was {minDraws})");
            foreach (var boss in GameLists.Act("Overgrowth").Bosses)
                c.That(Math.Abs(bosses.GetValueOrDefault(boss.ClassName) - runs / 3) < 110, $"{boss.ClassName} is the boss in {bosses.GetValueOrDefault(boss.ClassName)} of {runs} runs (a third each)");
            // Darv goes to act 2 with one chance in two and is then one of four Ancients there: 1 in 8.
            // Else it goes to act 3 with one chance in two, as one of four: 1 in 16.
            c.That(Math.Abs(darvInHive - runs / 8) < 60, $"Darv opens act 2 in {darvInHive} of {runs} runs (1 in 8)");
            c.That(Math.Abs(darvInGlory - runs / 16) < 45, $"Darv opens act 3 in {darvInGlory} of {runs} runs (1 in 16)");
        });

        c.Group("the hallway list keeps encounters with a shared tag apart where it can", () =>
        {
            var act = GameLists.Act("Overgrowth");
            var byName = act.Encounters.ToDictionary(e => e.ClassName, StringComparer.Ordinal);
            var rng = new GameRng(77);
            const int runs = 3000;
            var touching = 0;
            var listsWithTouching = 0;
            var borderTouching = 0;
            for (var n = 0; n < runs; n++)
            {
                var rooms = RunGeneration.GenerateRooms(act, new GameRng(rng.NextU64()), RunUnlocks.All, []);
                var inThisList = 0;
                for (var k = 1; k < rooms.NormalEncounters.Count; k++)
                {
                    if (!byName[rooms.NormalEncounters[k]].SharesTagsWith(byName[rooms.NormalEncounters[k - 1]])) continue;
                    inThisList++;
                    // The first regular fight looks at the last weak one, and there is always a regular encounter that fits.
                    if (k == act.WeakEncounters) borderTouching++;
                }
                touching += inThisList;
                if (inThisList > 0) listsWithTouching++;
            }
            c.Equal(0, borderTouching, "the first regular fight never shares a tag with the last weak one");
            // The spec's own simulation of the rule: 3.9% of lists hold one such pair, nearly always the last two fights.
            c.That(listsWithTouching > runs * 2 / 100 && listsWithTouching < runs * 6 / 100, $"{listsWithTouching} of {runs} lists hold a pair that shares a tag (about 3.9%)");
            c.That(touching <= listsWithTouching + 5, "hardly ever more than one pair in a list");
        });

        c.Group("the act's rooms: counters, wrapping and copies", () =>
        {
            var rooms = RunGeneration.Start(new RunStreams(PlayedSeed), RunUnlocks.All).Rooms[0];
            c.Equal("ShrinkerBeetleWeak", rooms.NextNormalEncounter, "the first Monster room's fight");
            rooms.MarkVisited(RoomType.Monster);
            c.Equal("FuzzyWurmCrawlerWeak", rooms.NextNormalEncounter, "the second");
            c.Equal("BygoneEffigyElite", rooms.NextEliteEncounter, "an Elite room does not use up a hallway fight");
            rooms.MarkVisited(RoomType.Elite);
            c.Equal("ByrdonisElite", rooms.NextEliteEncounter, "the second elite");
            rooms.NormalEncountersVisited = 15;
            c.Equal("ShrinkerBeetleWeak", rooms.NextNormalEncounter, "after the fifteenth the list starts again");
            c.Equal("TheFutureOfPotions", rooms.NextEvent, "the event list's first entry");
            rooms.MarkEventVisited();
            c.Equal("ThisOrThat", rooms.NextEvent, "its second");
            var copy = rooms.Clone();
            copy.MarkVisited(RoomType.Boss);
            copy.NormalEncounters[0] = "NibbitsWeak";
            c.That(rooms.BossEncountersVisited == 0 && rooms.NormalEncounters[0] == "ShrinkerBeetleWeak", "a copy changes alone");
            c.Equal(1, copy.EventsVisited, "and carries the counters");
        });
    }

    // ---------- the run's part of all this ----------

    private static void World(Check c)
    {
        c.Group("the run's world: made from the run's seed, kept in its state, copied with it", () =>
        {
            var state = new RunState(new RunSetup { Seed = "KMEHXZW0R5" });
            var empty = state.Part<RunWorld>();
            c.That(!empty.IsBegun, "a run that was not begun has an empty world");
            var threw = false;
            try { _ = empty.Streams; }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "and asking it for a stream says so");

            var world = RunWorld.Begin(state);
            c.That(ReferenceEquals(world, state.Part<RunWorld>()) && world.IsBegun, "Begin puts the world into the state");
            // The real save of this seed.
            c.Equal(411, world.UpFrontDraws, "numbers taken from up_front");
            c.Equal(411, world.Streams.Counter(RunStream.UpFront), "and the stream's counter says the same");
            c.Sequence(["Overgrowth", "Hive", "Glory"], world.Acts, "the acts");
            c.Equal("VantomBoss", world.Rooms[0].Boss, "the boss of act 1");
            c.Equal("TuningFork", world.PlayerBag.Relics(RelicRarity.Uncommon)[0], "front of the player's Uncommon list");
            c.Equal("PenNib", world.SharedBag.Relics(RelicRarity.Uncommon)[0], "front of the shared Uncommon list");
            c.Equal(0.1f, world.UnknownOdds.Monster, "the Unknown odds at their start");

            threw = false;
            try { world.Begin("KMEHXZW0R5"); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a world is begun once");

            var copy = state.Clone().Part<RunWorld>();
            c.That(!ReferenceEquals(copy, world), "a copy of the run has its own world");
            c.Equal(world.Dump()!.ToJsonString(), copy.Dump()!.ToJsonString(), "which dumps the same");
            copy.Streams.Draw(RunStream.Rewards, Purpose.Gold).NextU64();
            copy.PlayerBag.PullFromFront(RelicRarity.Uncommon);
            copy.Rooms[0].MarkVisited(RoomType.Monster);
            copy.UnknownOdds.Roll(NextBetween(0.9f, 0.95f), UnknownRoomOdds.AllRooms);
            c.Equal(0, world.Streams.Counter(RunStream.Rewards), "a draw in the copy leaves the run's streams alone");
            c.Equal("TuningFork", world.PlayerBag.Relics(RelicRarity.Uncommon)[0], "a pull in the copy leaves the run's bag alone");
            c.Equal(0, world.Rooms[0].NormalEncountersVisited, "and its rooms");
            c.Equal(0.1f, world.UnknownOdds.Monster, "and its odds");
            c.That(world.Dump()!.ToJsonString() != copy.Dump()!.ToJsonString(), "and now the two dump differently");

            var again = new RunState(new RunSetup { Seed = "kmehxzwor5" });
            c.Equal(world.Dump()!.ToJsonString(), RunWorld.Begin(again).Dump()!.ToJsonString(), "the same seed, typed in lower case with a letter O, gives the same world");
            var keyed = RunWorld.Begin(new RunState(new RunSetup { Seed = "KMEHXZW0R5" }), mode: StreamMode.Keyed);
            c.Sequence(world.Rooms[0].NormalEncounters, keyed.Rooms[0].NormalEncounters, "keyed streams change nothing the seed decides: the fights");
            c.Sequence(world.PlayerBag.Relics(RelicRarity.Rare), keyed.PlayerBag.Relics(RelicRarity.Rare), "nor the bags");
            c.Equal(411, keyed.Streams.Counter(RunStream.UpFront), "up_front is drawn from in keyed mode as well");
            c.Equal(StreamMode.Keyed, keyed.Streams.Mode, "the mode asked for");

            var forced = RunWorld.Begin(new RunState(new RunSetup { Seed = "KMEHXZW0R5" }), acts: ["Underdocks", "Hive", "Glory"]);
            c.Equal("Underdocks", forced.Rooms[0].Act, "acts that are named are not rolled");
        });

        c.Group("the two odds that are numbers of the run's state", () =>
        {
            var state = new RunState(new RunSetup { Seed = PlayedSeed });
            c.Equal(CardRarity.Common, RunWorld.RollCardRarity(state, NextBetween(0.9f, 0.95f), CardOddsType.Regular), "a card roll");
            c.Equal(-0.04f, state.CardRarityOffset, "moves the run's pity offset");
            c.Equal(CardRarity.Rare, RunWorld.RollCardRarity(state, NextBetween(0.9f, 0.95f), CardOddsType.Boss), "a boss card");
            c.Equal(-0.05f, state.CardRarityOffset, "puts it back");
            c.That(!RunWorld.RollPotionDrop(state, NextBetween(0.9f, 0.95f), elite: false), "a potion roll that fails");
            c.Equal(0.5f, state.PotionChance, "raises the run's potion chance");
            c.That(RunWorld.RollPotionDrop(state, NextBetween(0.9f, 0.95f), elite: false, forced: true), "a forced drop");
            c.Equal(0.4f, state.PotionChance, "lowers it");
        });
    }

    /// <summary>Two fights against a 5 HP dummy, each on the run's own long streams: lent, and taken back after the win.</summary>
    private sealed class OnTheRunsStreams : RunDirector
    {
        private readonly int _skip;
        private int _fights;

        /// <param name="skip">Numbers the run's shuffle stream has given before the first fight.</param>
        public OnTheRunsStreams(int skip = 0) => _skip = skip;

        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State).Streams.FastForward(RunStream.Shuffle, _skip);
            Next(run);
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight)
        {
            run.State.Part<RunWorld>().Streams.TakeBack(run.LastFightRng!);
            Next(run);
        }

        private void Next(Arena.Run run)
        {
            if (_fights++ >= 2)
            {
                run.End(RunResult.Cleared);
                return;
            }
            run.State.TotalFloor++;
            var streams = run.State.Part<RunWorld>().Streams;
            run.StartFight(new FightRequest
            {
                Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 5 })],
                Rng = streams.LendToFight("NibbitsWeak", run.State.TotalFloor),
            });
        }
    }

    /// <summary>
    /// The same two fights, but the director only begins the world and starts the fights: the run
    /// lends its streams and takes them back by itself (Run.StartFight).
    /// </summary>
    private sealed class LentByTheRun : RunDirector
    {
        private readonly StreamMode _mode;
        private readonly int? _seedOfFirst;
        private int _fights;

        /// <param name="seedOfFirst">A seed of its own for the first fight, which then leaves the run's streams alone.</param>
        public LentByTheRun(StreamMode mode = StreamMode.Game, int? seedOfFirst = null)
        {
            _mode = mode;
            _seedOfFirst = seedOfFirst;
        }

        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State, mode: _mode);
            Next(run);
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => Next(run);

        private void Next(Arena.Run run)
        {
            if (_fights++ >= 2)
            {
                run.End(RunResult.Cleared);
                return;
            }
            run.State.TotalFloor++;
            run.StartFight(new FightRequest
            {
                Encounter = "NibbitsWeak",
                Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 5 })],
                Seed = _fights == 1 ? _seedOfFirst : null,
            });
        }
    }

    private static void FightsOfARun(Check c)
    {
        static void WinFight(Arena.Run run)
        {
            // Five cards of the starter are drawn and at most four are Defends: an Attack is in hand, and any Attack kills a 5 HP dummy.
            var id = run.LegalActions().First(a => a.Id.EndsWith(">e0", StringComparison.Ordinal)).Id;
            if (!run.Execute(id).Ok) throw new InvalidOperationException($"{id} was refused");
        }

        c.Group("a run whose world is begun lends its fights the run's streams by itself and takes them back", () =>
        {
            var setup = new RunSetup { Seed = PlayedSeed };
            var run = new Arena.Run(setup, new LentByTheRun());
            var world = run.State.Part<RunWorld>();
            c.That(run.Top is FightScreen { OnRunStreams: true }, "the fight is on the run's streams");
            // Ten cards: the opening shuffle takes nine numbers. The fight works on copies, so the run's own stream waits.
            c.Equal(9, run.Fight!.Rng.Shuffle.Counter, "the first fight's shuffle stream after its opening shuffle");
            c.Equal(0, world.Streams.Counter(RunStream.Shuffle), "the run's own stream waits while the fight is on");
            var copy = run.Clone();
            WinFight(run);
            world = run.State.Part<RunWorld>();
            c.Equal(9, world.Streams.Counter(RunStream.Shuffle), "after the win the run's shuffle stream has given the nine numbers, with no call of the director");
            c.Equal(18, run.Fight!.Rng.Shuffle.Counter, "the second fight shuffled with the next nine");
            c.Equal(9, world.Streams.Counter(RunStream.Shuffle), "and the run's stream waits again");

            // The same run with the streams lent and taken back by hand (the check below) is in the same state.
            var byHand = new Arena.Run(setup, new OnTheRunsStreams());
            WinFight(byHand);
            c.Sequence(byHand.Fight!.HandTitles(), run.Fight!.HandTitles(), "the second fight's hand is the one a director gets that lends by hand");
            WinFight(run);
            WinFight(byHand);
            c.That(run.AtEnd && run.Result == RunResult.Cleared, "both fights won");
            c.Equal(byHand.State.Dump().ToJsonString(), run.State.Dump().ToJsonString(), "lent by the run or by hand: the same end state, every stream counter included");
            c.Equal(18, run.State.Part<RunWorld>().Streams.Counter(RunStream.Shuffle), "eighteen numbers of the shuffle stream in all");

            // A copy taken in the middle of the first fight goes the same way, and so does a replay of the save.
            WinFight(copy);
            WinFight(copy);
            c.Equal(run.Dump(), copy.Dump(), "a copy taken during the first fight ends in the same state");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new LentByTheRun(), run.Actions).Dump(), "and so does a replay of the action list");
        });

        c.Group("a fight with a seed of its own leaves the run's streams alone; a keyed run's fights start afresh", () =>
        {
            var setup = new RunSetup { Seed = PlayedSeed };
            var run = new Arena.Run(setup, new LentByTheRun(seedOfFirst: 5));
            c.That(run.Top is FightScreen { OnRunStreams: false }, "a fight that names a seed is not on the run's streams");
            WinFight(run);
            c.Equal(0, run.State.Part<RunWorld>().Streams.Counter(RunStream.Shuffle), "after it the run's shuffle stream has given nothing");
            c.That(run.Top is FightScreen { OnRunStreams: true }, "the second fight, without a seed, is on the run's streams");
            c.Equal(9, run.Fight!.Rng.Shuffle.Counter, "and shuffles with the stream's first nine numbers");

            var keyed = new Arena.Run(setup, new LentByTheRun(StreamMode.Keyed));
            c.Equal(9, keyed.Fight!.Rng.Shuffle.Counter, "keyed mode: the first fight's own shuffle stream gave nine numbers");
            WinFight(keyed);
            c.Equal(0, keyed.State.Part<RunWorld>().Streams.Counter(RunStream.Shuffle), "keyed mode: the long shuffle stream is never used");
            c.Equal(9, keyed.Fight!.Rng.Shuffle.Counter, "keyed mode: the second fight starts a stream of its own, nine numbers in after its shuffle");
            // That stream is the place's own: the second stream of its purpose at the place (nothing moved the
            // run, so both fights stand on the same node), nine numbers in.
            var fresh = new RunStreams(PlayedSeed, StreamMode.Keyed);
            fresh.LendToFight("NibbitsWeak", 1);
            var second = fresh.LendToFight("NibbitsWeak", 2).Shuffle;
            while (second.Counter < 9) second.NextU64();
            c.Equal(second.NextU64(), keyed.Fight!.Rng.Shuffle.Clone().NextU64(), "keyed mode: its tenth number is the tenth of the place's second fight stream");
        });

        c.Group("fights of a run on the run's long streams: the second shuffle goes on where the first stopped", () =>
        {
            static void Win(Arena.Run run)
            {
                // Five cards of the starter are drawn and at most four are Defends: an Attack is in hand, and any Attack kills a 5 HP dummy.
                var id = run.LegalActions().First(a => a.Id.EndsWith(">e0", StringComparison.Ordinal)).Id;
                if (!run.Execute(id).Ok) throw new InvalidOperationException($"{id} was refused");
            }

            var setup = new RunSetup { Seed = PlayedSeed };
            var run = new Arena.Run(setup, new OnTheRunsStreams());
            // Ten cards: the opening shuffle takes nine numbers.
            c.Equal(9, run.Fight!.Rng.Shuffle.Counter, "the first fight's shuffle stream after its opening shuffle");
            c.Equal(0, run.State.Part<RunWorld>().Streams.Counter(RunStream.Shuffle), "the run's own stream waits");
            Win(run);
            c.Equal(9, run.State.Part<RunWorld>().Streams.Counter(RunStream.Shuffle), "after the win the run's shuffle stream has given the nine numbers");
            c.Equal(18, run.Fight!.Rng.Shuffle.Counter, "the second fight shuffled with the next nine");

            // A run whose shuffle stream had given nine numbers before its first fight shuffles the same deck the same way.
            var late = new Arena.Run(setup, new OnTheRunsStreams(skip: 9));
            c.Sequence(late.Fight!.HandTitles(), run.Fight!.HandTitles(), "the second fight's hand is the one the stream gives nine numbers in");
            c.Equal(2, run.State.TotalFloor, "and the encounter's own stream was asked for floor 2");

            Win(run);
            c.That(run.AtEnd && run.Result == RunResult.Cleared, "both fights won");
            c.Equal(18, run.State.Part<RunWorld>().Streams.Counter(RunStream.Shuffle), "eighteen numbers of the shuffle stream in all");

            // The world is copied with the run before every action and rebuilt by a replay of the save.
            var replay = Arena.Run.Replay(setup, new OnTheRunsStreams(), run.Actions);
            c.Equal(run.State.Dump().ToJsonString(), replay.State.Dump().ToJsonString(), "a replay of the action list ends in the same state, streams and bags included");
            c.That(run.State.Dump().ToJsonString().Contains("\"up_front\":419"), "and the state's dump carries the world (up_front at 419 for this seed, as in the real save)");
        });
    }

    // ---------- what a profile has unlocked ----------

    private static void Unlocks(Check c)
    {
        var encounters = GameLists.Acts.SelectMany(a => a.Encounters).Select(e => e.ClassName).ToList();
        string[] acts = ["Overgrowth", "Underdocks", "Hive", "Glory"];

        c.Group("a profile without an epoch does not meet what the epoch unlocks, and the streams shift with it", () =>
        {
            var all = RunGeneration.Generate(new RunStreams(PlayedSeed), ["Overgrowth", "Hive", "Glory"], RunUnlocks.All);

            var noRelic1 = new RunUnlocks(GameLists.Epochs.Where(e => e != "RELIC1_EPOCH"), encounters, 50, acts);
            var start = RunGeneration.Generate(new RunStreams(PlayedSeed), ["Overgrowth", "Hive", "Glory"], noRelic1);
            c.Equal(115, start.SharedBag.Rarities.Sum(r => start.SharedBag.Relics(r).Count), "three relics fewer in the shared bag");
            c.That(new[] { "UnsettlingLamp", "IntimidatingHelmet", "ReptileTrinket" }.All(r => !start.SharedBag.Contains(r) && !start.PlayerBag.Contains(r)), "the three of that epoch are in neither bag");
            // Two Rare relics and one Uncommon fewer in each bag: six numbers fewer for the shuffles.
            c.That(start.Rooms[0].Events.Count == 31, "the events are untouched");

            var noIronclad3 = new RunUnlocks(GameLists.Epochs.Where(e => e != "IRONCLAD3_EPOCH"), encounters, 50, acts);
            var bag = RunGeneration.Generate(new RunStreams(PlayedSeed), ["Overgrowth", "Hive", "Glory"], noIronclad3).PlayerBag;
            c.That(!bag.Contains("RedSkull") && !bag.Contains("PaperPhrog") && !bag.Contains("RuinedHelmet") && bag.Contains("CharonsAshes"), "an Ironclad epoch gates three of the Ironclad's relics");
            c.Equal(119, bag.Rarities.Sum(r => bag.Relics(r).Count), "119 relics in that player's bag");

            var noEvents = new RunUnlocks(GameLists.Epochs.Where(e => e is not ("EVENT1_EPOCH" or "EVENT2_EPOCH" or "EVENT3_EPOCH")), encounters, 50, acts);
            var docks = RunGeneration.Generate(new RunStreams(PlayedSeed), ["Underdocks", "Hive", "Glory"], noEvents);
            c.That(docks.Rooms[0].Events.Count == 27 && !docks.Rooms[0].Events.Contains("TrashHeap"), "Underdocks without Trash Heap");
            c.That(docks.Rooms[1].Events.Count == 27 && !docks.Rooms[1].Events.Contains("ColorfulPhilosophers"), "Hive without Colorful Philosophers");
            c.That(docks.Rooms[2].Events.Count == 24 && !docks.Rooms[2].Events.Contains("Reflections"), "Glory without Reflections");

            var noAncients = new RunUnlocks(GameLists.Epochs.Where(e => e is not ("NEOW_EPOCH" or "OROBAS_EPOCH" or "DARV_EPOCH")), encounters, 50, acts);
            var bare = RunGeneration.Generate(new RunStreams(PlayedSeed), ["Overgrowth", "Hive", "Glory"], noAncients);
            c.Equal(null, bare.Rooms[0].Ancient, "no Neow before its epoch");
            c.That(bare.Rooms[1].Ancient is "Pael" or "Tezcatara", "Hive without Orobas and Darv");
            // The bags and the lists before the first Ancient pick are the same; one number fewer is drawn for the act 1 Ancient.
            c.Sequence(all.Rooms[0].NormalEncounters, bare.Rooms[0].NormalEncounters, "act 1's fights do not depend on the Ancients");
            c.Equal(all.Rooms[0].Boss, bare.Rooms[0].Boss, "nor its boss");
        });

        c.Group("a boss the profile has never met is forced; nothing is drawn for it", () =>
        {
            var seen = encounters.Where(e => e is not ("CeremonialBeastBoss" or "TheKinBoss" or "KnowledgeDemonBoss")).ToList();
            var unlocks = new RunUnlocks(GameLists.Epochs, seen, 50, acts);
            var all = RunGeneration.Generate(new RunStreams("KMEHXZW0R5"), ["Overgrowth", "Hive", "Glory"], RunUnlocks.All);
            var start = RunGeneration.Generate(new RunStreams("KMEHXZW0R5"), ["Overgrowth", "Hive", "Glory"], unlocks);
            // Discovery order Vantom, Ceremonial Beast, The Kin: the first one not met.
            c.Equal("CeremonialBeastBoss", start.Rooms[0].Boss, "act 1: the first unmet boss of the order");
            c.Equal("KnowledgeDemonBoss", start.Rooms[1].Boss, "act 2: Knowledge Demon, the only unmet one");
            c.Equal(all.Rooms[2].Boss, start.Rooms[2].Boss, "act 3: all met, the rolled boss stays");
            c.Equal(all.UpFrontDraws, start.UpFrontDraws, "the same numbers were drawn");
            c.Sequence(all.Rooms[1].NormalEncounters, start.Rooms[1].NormalEncounters, "and the lists after it are the same");
        });

        c.Group("a profile's very first run: Overgrowth's lists start in a fixed order", () =>
        {
            // Every epoch revealed, so the lists that were rolled are the real save's (seed KMEHXZW0R5);
            // no run finished and no encounter met. The game then puts seven fights, two events and two
            // elites in place, one after the other: the first entry of that name changes places with the
            // entry at the index, and a name the list does not hold is written over the entry there.
            var first = new RunUnlocks(GameLists.Epochs, [], 0, acts);
            var rooms = RunGeneration.Generate(new RunStreams("KMEHXZW0R5"), ["Overgrowth", "Hive", "Glory"], first).Rooms;
            // Rolled: Slimes, Fuzzy Wurm Crawler, Nibbits (weak), Fogmog, Ruby Raiders, Slithering Strangler, Snapping
            // Jaxfruit, Cubex Construct, Mawler, Overgrowth Crawlers, Nibbits, Inklets, Vine Shambler, Flyconid, Slimes.
            // Traced by hand: Nibbits (weak) 2 to 0; Slimes (weak) 2 to 1; Shrinker Beetle is not in the list and
            // overwrites entry 2 (the Fuzzy Wurm Crawler); Inklets 11 to 3; Mawler 8 to 4; Ruby Raiders 8 to 5; Nibbits 10 to 6.
            c.Sequence(
                [
                    "NibbitsWeak", "SlimesWeak", "ShrinkerBeetleWeak", "InkletsNormal", "MawlerNormal", "RubyRaidersNormal", "NibbitsNormal", "CubexConstructNormal",
                    "SlitheringStranglerNormal", "OvergrowthCrawlers", "SnappingJaxfruitNormal", "FogmogNormal", "VineShamblerNormal", "FlyconidNormal", "SlimesNormal",
                ],
                rooms[0].NormalEncounters, "the hallway fights");
            // Rolled: Bygone Effigy, Phrog Parasite, Byrdonis, Bygone Effigy, ... Byrdonis 2 to 0; Phrog Parasite is at 1 already.
            // The game does not mind that this leaves the same elite twice in a row.
            c.Sequence(["ByrdonisElite", "PhrogParasiteElite", "BygoneEffigyElite", "BygoneEffigyElite", "ByrdonisElite"], rooms[0].EliteEncounters.Take(5), "the first five elites");
            // Rolled: Self Help Book, Fake Merchant, Morphic Grove, Unrest Site, ..., Byrdonis Nest at 20, Sapphire Seed at 23.
            c.Sequence(["ByrdonisNest", "SapphireSeed", "MorphicGrove", "UnrestSite"], rooms[0].Events.Take(4), "the first four events");
            c.Equal("SelfHelpBook", rooms[0].Events[20], "the event that stood first is where Byrdonis Nest was");
            c.Equal("FakeMerchant", rooms[0].Events[23], "the event that stood second is where Sapphire Seed was");
            c.Equal("VantomBoss", rooms[0].Boss, "the boss: Vantom, first of the discovery order");
            c.Equal("TheInsatiableBoss", rooms[1].Boss, "act 2: The Insatiable");
            c.Equal("QueenBoss", rooms[2].Boss, "act 3: Queen");
            c.Sequence(["ExoskeletonsWeak", "TunnelerWeak", "BowlbugsNormal", "SpinyToadNormal"], rooms[1].NormalEncounters.Take(4), "the other acts keep the order that was rolled (the save's)");
        });
    }
}
