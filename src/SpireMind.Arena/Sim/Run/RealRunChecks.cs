using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The first floors of a real run, with the pieces of run mode working together: the world of
/// the seed, the map and its screen, fights of the act's own list on the run's streams, their
/// rewards through the rewards and card reward screens, cards going into the deck, an Unknown
/// room, a relic pull, a curse, the shop and the run's hooks. Each piece has its own checks;
/// this one holds that they fit, on one run, through the screens a player sees. Then whole
/// acts are walked through every room that is built, to hold that the rooms fit each other.
/// <para>
/// Every expected value of the real run is the real game's: Mark's run on seed 1BUSLN0FY5
/// (v0.107.1), as its saves and the game's own record of it have it (sts2-notes/golden-saves:
/// the map of the save at Neow, history/1791755111.run, the later saves). Neow offered Precise
/// Scissors, Arcane Scroll and Leafy Poultice; Arcane Scroll was taken and gave Barricade.
/// Floor 2, at (5,1): Shrinker Beetle; 11 gold, no potion, the cards Colossus (taken),
/// Unrelenting, Havoc. Floor 3, at (6,2): Fuzzy Wurm Crawler; 10 gold, a Vulnerable Potion, the
/// cards Fight Me! (taken), Stone Armor, Setup Strike. Floor 4, at (6,3): an Unknown room that
/// was an event, This or That, which gave the relic Stone Cracker and the curse Clumsy. Floor
/// 5, at (6,4): a shop with Tear Asunder (on sale for 77 gold, bought: 120 gold became 43),
/// Ashen Strike, Tremble, Blood Wall, Vicious; Discovery, Calamity; Paper Phrog, Pendulum,
/// Ringing Triangle; three potions. Floor 6: the hallway list's third fight, Twig Slime (S),
/// Twig Slime (M), Leaf Slime (S). After the fights the <c>rewards</c> stream stood at 12 and
/// 25, at 26 after the event's relic; <c>niche</c> stood at 1 and 2 after the two fights.
/// </para>
/// <para>
/// How the fights are played does not show in any of that: a fight draws from the run's other
/// streams, never from <c>rewards</c>. They are played by the Greedy player. Every room is
/// the built one: Neow's room is the registry's own (her offer on this seed is three built
/// relics, and the check takes Arcane Scroll as the real run did), the event of floor 4 is the
/// built This or That? (the check takes its second option, as the real run did), and so are
/// the shop, the rest site and the treasure room.
/// </para>
/// <para>
/// Where the arena has not built what the game gave (Stone Cracker, the shop's colorless cards
/// and relics), the run without the strict switch takes the next built thing by the house
/// rule, and the check holds that against the order of the game's own lists as the saves have
/// them: nothing here is taken from what the arena's code returns.
/// </para>
/// </summary>
public sealed class RealRunChecks : IArenaChecks
{
    private const string PlayedSeed = "1BUSLN0FY5";

    public void Run(Check c)
    {
        FirstFloors(c);
        StrictRun(c);
        TheListsAgree(c);
        WholeActs(c);
    }

    // ---------- the director of the check ----------

    /// <summary>
    /// The act as far as it is built: Neow's room on the start point, the map, the fights of the
    /// act's lists with their rewards, the shop, the rest site, the treasure room and the event
    /// rooms with the events that are built. The act ends at the proceed of the boss's rewards.
    /// (The act's own director, with its modes and its floor log, is <see cref="ActDirector"/>;
    /// this one is kept small and apart from it, so that the two hold each other.)
    /// </summary>
    private sealed class Floors : RunDirector, IMapDirector
    {
        /// <summary>The game's streams with nothing drawn again.</summary>
        public bool Strict { get; init; }

        public List<string> Encounters = new();
        public string? Event;

        public override void Begin(Arena.Run run)
        {
            var world = RunWorld.Begin(run.State);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            var map = MapState.BeginAct(run, 0);
            var arrival = map.Enter(run, map.Map.Start.Coord);

            // The start point holds the act's Ancient, Neow. Her room tells the relics that it was entered, and its
            // Leave option finishes the room (AfterRoom opens the map).
            EventScreen.Open(run, world.Rooms[run.State.ActIndex].Ancient!);
            MapState.MarkRoomVisited(run, arrival.Room);
        }

        public void EnterRoom(Arena.Run run, MapArrival arrival)
        {
            var rooms = run.State.Part<RunWorld>().Rooms[run.State.ActIndex];
            switch (arrival.Room)
            {
                case RoomKind.Monster or RoomKind.Elite or RoomKind.Boss:
                    // The act's next list entry; the list's counter moves once the room is entered.
                    var encounter = arrival.Room switch
                    {
                        RoomKind.Monster => rooms.NextNormalEncounter,
                        RoomKind.Elite => rooms.NextEliteEncounter,
                        _ => rooms.Boss,
                    };
                    Encounters.Add(encounter);
                    run.StartFight(new FightRequest { Encounter = encounter });
                    MapState.MarkRoomVisited(run, arrival.Room);
                    return;
                case RoomKind.Shop:
                    ShopRoom.Enter(run);
                    MapState.MarkRoomVisited(run, arrival.Room);
                    return;
                case RoomKind.RestSite:
                    RestSite.Enter(run);
                    MapState.MarkRoomVisited(run, arrival.Room);
                    return;
                case RoomKind.Treasure:
                    TreasureRoom.Enter(run);
                    MapState.MarkRoomVisited(run, arrival.Room);
                    return;
                default:
                    // An Event room: the act's next event that is allowed, unmet and built (the game's own, built or not, is noted).
                    Event = EventList.Peek(run, static _ => true).GameEvent;
                    EventScreen.Open(run, EventList.Pull(run));
                    MapState.MarkRoomVisited(run, arrival.Room);
                    return;
            }
        }

        /// <summary>An Unknown point can be an Event room while a built event is allowed and unmet (in a strict run always: it then ends in the room).</summary>
        public bool CanHold(Arena.Run run, RolledRoom room) => room != RolledRoom.Event || EventList.CanHold(run);

        public override void AfterFightWon(Arena.Run run, FightOutcome fight)
        {
            if (RunRewards.OfferForRoomEnd(run, fight) is null) AfterRoom(run);
        }

        public override void AfterRoom(Arena.Run run)
        {
            // The room on the boss's point was finished: the act is cleared. The map is never opened there.
            if (run.State.Part<MapState>().CurrentPoint?.Type == MapPointType.Boss)
            {
                run.End(RunResult.Cleared);
                return;
            }
            run.CloseAll();
            run.Push(new MapScreen());
        }

        public override RunDirector Clone(CloneMap map) => new Floors { Strict = Strict, Encounters = Encounters.ToList(), Event = Event };
    }

    // ---------- helpers ----------

    private static readonly IPolicy Greedy = Policies.All.Single(policy => policy.Name == "Greedy");

    /// <summary>Plays the fight that is on to its end.</summary>
    private static void PlayFight(Arena.Run run)
    {
        for (var n = 0; n < 2000 && run.Fight is not null; n++) Do(run, Greedy.Choose(run.Snapshot()));
    }

    private static RunWorld World(Arena.Run run) => run.State.Part<RunWorld>();

    private static int Niche(Arena.Run run) => World(run).Streams.Counter(RunStream.Niche);

    /// <summary>What the run's log holds as passed over. (FindPart: asking with Part would make an empty log, which a replay does not have.)</summary>
    private static IReadOnlyList<LeftOutLog.Entry> Logged(Arena.Run run) => run.State.FindPart<LeftOutLog>()?.Entries ?? [];

    private static List<string> LoggedOn(Arena.Run run, int floor, string kind) =>
        Logged(run).Where(entry => entry.Floor == floor && entry.Kind == kind).Select(entry => entry.ClassName).ToList();

    // The player's relic bag of seed 1BUSLN0FY5 as the game saved it (golden-saves/run-164514-abe3bfe862ef.save,
    // players[0].relic_grab_bag), as far as this run reaches into it. The front of the Uncommon list, in order:
    private static readonly string[] UncommonFront =
        ["StoneCracker", "ParryingShield", "OrnamentalFan", "Kusarigama", "PenNib", "Pantograph", "SelfFormingClay", "Nunchaku", "MiniatureCannon", "MercuryHourglass"];

    // and the backs of three lists, from the last entry inwards, without the two a shop never sells (the Uncommon
    // list ends ... Eternal Feather, Pear, Sparkling Rouge, Paper Phrog, Lucky Fysh, Bowler Hat; the Common list ...
    // Bag of Marbles, Anchor, Pendulum; the Shop list ... Sling of Courage, Wing Charm, Orrery, Ringing Triangle).
    // The shop of floor 5 rolled Uncommon and Common for its first two relics; the third is always of the Shop list.
    private static readonly string[][] ShopRelicBacks =
    [
        ["PaperPhrog", "SparklingRouge", "Pear"],
        ["Pendulum", "Anchor"],
        ["RingingTriangle", "Orrery", "WingCharm", "SlingOfCourage"],
    ];

    /// <summary>The relic a pull gives from a list in the game's order: the first one the arena holds; the ones before it are passed over.</summary>
    private static (string Given, string[] PassedOver) FirstBuilt(string[] gameOrder)
    {
        var given = gameOrder.First(RunRelics.IsBuilt);
        return (given, gameOrder.TakeWhile(relic => relic != given).ToArray());
    }

    private static string TitleOfRelic(string className) => Content.Registry.NewRelic(className).Name;

    private static int RewardsCounter(Arena.Run run) => World(run).Streams.Counter(RunStream.Rewards);

    private static string NextPlaces(GameSnapshot map) => string.Join(" ", map.State["next"]!.AsArray().Select(option => $"{option!["at"]} {option["type"]}"));

    private static List<string> Rows(GameSnapshot rewards) =>
        rewards.State["rewards"]!.AsArray().Select(row => row!["kind"]!.GetValue<string>() switch
        {
            "gold" => $"gold {row["amount"]}",
            "potion" => $"potion {row["name"]}",
            "card" => $"card x{row["count"]}",
            var other => other,
        }).ToList();

    private static List<string> Offered(GameSnapshot cardReward) => cardReward.State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()).ToList();

    /// <summary>Takes the gold, then opens the card reward (the last row) and returns its screen.</summary>
    private static GameSnapshot TakeGoldAndOpenCards(Arena.Run run)
    {
        Do(run, "reward:0");
        Do(run, $"reward:{run.Snapshot().State["rewards"]!.AsArray().Count - 1}");
        return run.Snapshot();
    }

    // ---------- the run ----------

    private static void FirstFloors(Check c)
    {
        c.Group("the real run on seed 1BUSLN0FY5 from Neow's room to its first shop: map, fights, rewards, deck and relic pull together", () =>
        {
            var setup = new RunSetup { Seed = PlayedSeed };
            var run = new Arena.Run(setup, new Floors());

            // Floor 1: the start point, Neow's room. (The world is asked for anew each time: a run that
            // was asked a question goes on from a copy of its state.)
            c.Equal("Overgrowth", World(run).Acts[0], "the seed's first act");
            var s = run.Snapshot();
            c.Equal("event", s.Screen, "the run begins in Neow's room");
            c.Equal("Neow", s.State["event"]!.GetValue<string>(), "the Ancient of the act's start point");
            c.Sequence(["Precise Scissors", "Arcane Scroll", "Leafy Poultice"], s.State["options"]!.AsArray().Select(option => option!["title"]!.GetValue<string>()),
                "she offers the three relics of the game's record, in its order");
            c.Equal(0, RewardsCounter(run), "nothing on her screen draws from rewards");
            c.Sequence([], Logged(run).Select(entry => entry.ClassName), "all three are built: nothing was passed over");
            Do(run, "option:1");
            c.Sequence(["BurningBlood", "ArcaneScroll"], run.State.Relics.Select(held => held.ClassName), "the real run took Arcane Scroll");
            s = run.Snapshot();
            c.Equal("option:0=Leave", Labels(s), "Neow is finished: the room's Leave is all that is left");
            Do(run, "option:0");
            s = run.Snapshot();
            c.Equal("map", s.Screen, "after Neow's room the map is up");
            c.Equal("""{"act":1,"floor":1,"ascension":0}""", Text(s.State["run"]), "on floor 1");
            c.Equal("Vantom", s.State["boss"]!.GetValue<string>(), "the act's boss");
            c.Equal("(3,0) ancient", s.State["at"]!.GetValue<string>(), "standing on the start point");
            c.Equal("(1,1) monster (3,1) monster (5,1) monster", NextPlaces(s), "the three first-row points of the saved map");
            c.Equal("map:0 map:1 map:2", Ids(s), "one action each; no potion is held");
            c.Sequence(["Bash", "Barricade"], run.State.Deck.TakeLast(2).Select(card => card.Def.ClassName), "Arcane Scroll's card is the deck's last");
            c.Equal(1, RewardsCounter(run), "and took one number of rewards");
            c.Equal(RoomKind.Event, run.Room, "the room the hooks were told about is an Event room");
            c.Equal(1, World(run).Rooms[0].EventsVisited, "Neow's room moved the act's event counter");
            c.Sequence(["PreciseScissors", "ArcaneScroll", "LeafyPoultice"], NeowOffer.OfTheGame(World(run).Streams, World(run).Unlocks),
                "Neow's offer out of the seed: the three relics of the game's record, in its order");
            c.Equal("Neow", World(run).Rooms[0].Ancient, "and the Ancient of the act's start point is Neow");

            // Floor 2: the first hallway fight of the act's list, on the run's streams.
            Do(run, "map:2");
            c.Equal("combat", run.Snapshot().Screen, "travelling to a Monster point starts its fight");
            c.Equal(2, run.State.TotalFloor, "on floor 2");
            c.Equal("ShrinkerBeetleWeak", run.Fight!.EncounterName, "the first entry of the act's hallway list");
            c.That(((FightScreen)run.Top!).OnRunStreams, "on the run's own streams");
            PlayFight(run);
            c.That(run.Fight is null && !run.IsOver, "the fight is won");
            c.Equal(1, Niche(run), "niche after the first fight: one number, the monster's HP (the save made on entering floor 3 has 1)");

            s = run.Snapshot();
            c.Equal("rewards", s.Screen, "its rewards are up");
            c.Sequence(["gold 11", "card x3"], Rows(s), "floor 2: 11 gold, no potion, a card reward");
            c.Equal("Vantom", s.State["boss"]!.GetValue<string>(), "the boss is named on the rewards too");
            s = TakeGoldAndOpenCards(run);
            c.Equal(110, run.Gold, "99 + 11");
            c.Equal("card_reward", s.Screen, "the card reward's screen");
            var offer = Offered(s);
            c.Sequence(["Colossus", "Havoc", "Unrelenting"], offer.Order(StringComparer.Ordinal), "floor 2: the three cards");
            c.That(offer.IndexOf("Unrelenting") < offer.IndexOf("Havoc"), "Unrelenting was offered before Havoc");
            Do(run, $"card:{offer.IndexOf("Colossus")}");
            c.Equal(12, RewardsCounter(run), "rewards after floor 2");
            c.Equal(-0.02f, run.State.CardRarityOffset, "the pity offset after three cards that were not Rare");
            c.Equal(0.5f, run.State.PotionChance, "the potion chance after no drop");
            Do(run, "proceed");

            // Floor 3.
            s = run.Snapshot();
            c.Equal("map", s.Screen, "proceed opens the map");
            c.Equal("(5,1) monster", s.State["at"]!.GetValue<string>(), "standing where the fight was");
            c.Equal("(6,2) monster", NextPlaces(s), "one way on");
            Do(run, "map:0");
            c.Equal("FuzzyWurmCrawlerWeak", run.Fight!.EncounterName, "the second entry of the hallway list");
            PlayFight(run);
            c.That(run.Fight is null && !run.IsOver, "the second fight is won");
            c.Equal(2, Niche(run), "niche after the second fight (the save made on entering floor 4 has 2)");

            s = run.Snapshot();
            c.Sequence(["gold 10", "potion Vulnerable Potion", "card x3"], Rows(s), "floor 3: 10 gold, a Vulnerable Potion, a card reward");
            Do(run, "reward:1");
            c.Sequence(["VulnerablePotion", null, null], run.State.Belt.Slots.Select(potion => potion?.ClassName), "the potion goes into the first slot");
            s = TakeGoldAndOpenCards(run);
            c.Equal(120, run.Gold, "110 + 10");
            offer = Offered(s);
            c.Sequence(["Fight Me!", "Setup Strike", "Stone Armor"], offer.Order(StringComparer.Ordinal), "floor 3: the three cards");
            c.That(offer.IndexOf("Stone Armor") < offer.IndexOf("Setup Strike"), "Stone Armor was offered before Setup Strike");
            Do(run, $"card:{offer.IndexOf("Fight Me!")}");
            c.Equal(25, RewardsCounter(run), "rewards after floor 3");
            c.Equal(0.01f, run.State.CardRarityOffset, "the pity offset");
            c.Equal(0.4f, run.State.PotionChance, "the potion chance after a drop");
            c.Sequence(["Bash", "Barricade", "Colossus", "FightMe"], run.State.Deck.TakeLast(4).Select(card => card.Def.ClassName), "the deck as the game's save has it after floor 3");
            c.Equal(13, run.State.Deck.Count, "thirteen cards");
            Do(run, "proceed");

            // Floor 4: the Unknown point, which the game rolled to be an event.
            s = run.Snapshot();
            c.Equal("(6,3) unknown", NextPlaces(s), "the next point is an Unknown one");
            c.Equal("map:0 discard_potion:0", Ids(s), "the map's action, then the potion's: it needs an enemy, so only the discard is offered");
            Do(run, "map:0");
            s = run.Snapshot();
            c.Equal("event", s.Screen, "the Unknown room is an event");
            c.Equal("This or That?", s.State["event"]!.GetValue<string>(), "the event of the game's record");
            c.Equal("""[{"n":0,"title":"This","text":"Lose 6 HP. Gain 61 Gold."},{"n":1,"title":"That","text":"Obtain a random Relic. Add Clumsy to your Deck.","cards":[{"name":"Clumsy","cost":"-","type":"Curse","text":"Unplayable. / Ethereal."}]}]""",
                Text(s.State["options"]), "its two options: 6 HP for the gold the seed rolls, or a random relic and the curse Clumsy");
            c.Equal("option:0 option:1 discard_potion:0", Ids(s), "one action each, then the held potion's discard");
            c.Equal(4, run.State.TotalFloor, "on floor 4");
            var odds = World(run).UnknownOdds;
            c.Equal((0.2f, -2f, 0.04f, 0.06f), (odds.Monster, odds.Elite, odds.Treasure, odds.Shop), "the odds of the next Unknown room, as the later save has them");
            c.Equal("ThisOrThat", ((Floors)run.Director).Event, "the event the game takes from the act's list: entry 1, since Neow's room took the counter to 1");
            c.Equal(2, World(run).Rooms[0].EventsVisited, "and the counter is at 2 now");
            c.Equal(RoomKind.Event, run.Room, "the hooks were told an Event room");

            // What the real run chose, the second option (the record's ORNATE): a relic from the front of the player's bag, then the curse
            // Clumsy. The 26th number of rewards rolls Uncommon, and the front of the player's Uncommon list is Stone Cracker.
            Do(run, "option:1");
            c.Equal(26, RewardsCounter(run), "one number for the rarity");
            c.Equal("""[{"n":0,"title":"Proceed","leave":true}]""", Text(run.Snapshot().State["options"]), "the event is finished: the room's own option is left");
            c.Equal((80, 120), (run.Hp, run.Gold), "HP and gold as the record has them after the room: 80 HP, 120 gold");
            var (relic, passedOver) = FirstBuilt(UncommonFront);
            c.Sequence(["BurningBlood", "ArcaneScroll", relic], run.State.Relics.Select(held => held.ClassName), "the relic given is the first of the saved Uncommon list that the arena holds, Stone Cracker itself once it is built");
            c.Sequence(passedOver, LoggedOn(run, 4, "relic"), "and every relic before it in the game's list is in the run's log as passed over on floor 4");
            c.Equal(passedOver.Length > 0 ? 4 : null, run.State.FindPart<LeftOutLog>()?.FirstFloor, "which makes floor 4 the first floor on which the run is not the game's own (none while Stone Cracker is built)");
            c.That(!World(run).PlayerBag.Contains(relic) && !World(run).SharedBag.Contains(relic), "the relic has left both bags");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash Barricade Colossus FightMe Clumsy",
                DeckNames(run), "the deck as the game's save has it on entering floor 5: the curse Clumsy is its fourteenth card");
            c.Equal("""{"name":"Clumsy","cost":"-","type":"Curse","text":"Unplayable. / Ethereal.","rarity":"Curse"}""", Text(run.DescribeDeck()["deck"]![13]), "and get_deck shows it last");
            c.Sequence(["ThisOrThat"], run.State.Part<EventsState>().Visited, "This or That? is met (the save's events_seen)");
            Do(run, "option:0");

            // Floor 5: the shop, through its own screen.
            s = run.Snapshot();
            c.Equal("(5,4) monster (6,4) shop", NextPlaces(s), "two ways on from the event's point, as the saved map has them");
            Do(run, "map:1");
            s = run.Snapshot();
            c.Equal("shop", s.Screen, "the shop of floor 5 is up");
            c.Equal(5, run.State.TotalFloor, "on floor 5");
            c.Equal(RoomKind.Shop, run.Room, "the hooks were told a Shop room");
            c.Equal("run,boss,gold,inventory_open,player,potions,stock", Keys(s.State), "the shop's keys in the mod's order");
            c.Equal(120, s.State["gold"]!.GetValue<int>(), "120 gold, as the record has it before the purchase");
            c.That(!s.State["inventory_open"]!.GetValue<bool>(), "the inventory is closed on arrival");
            c.Equal("open proceed discard_potion:0", Ids(s), "open, proceed, and the held potion's discard");
            var stock = s.State["stock"]!;
            c.Sequence(["Tear Asunder", "Ashen Strike", "Tremble", "Blood Wall", "Vicious"], stock["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()),
                "the five character cards of the game's record, in shelf order (the one that was bought first)");
            c.Equal("""{"price":77,"on_sale":true,"affordable":true}""",
                Text(new JsonObject { ["price"] = stock["cards"]![0]!["price"]!.DeepClone(), ["on_sale"] = stock["cards"]![0]!["on_sale"]?.DeepClone(), ["affordable"] = stock["cards"]![0]!["affordable"]!.DeepClone() }),
                "Tear Asunder is the card on sale, at the 77 gold the record says was spent");
            c.Equal(1, stock["cards"]!.AsArray().Count(card => card!["on_sale"] is not null), "one card is on sale");
            c.Equal(40, RewardsCounter(run), "the shelf took 14 numbers of rewards: five rarities, seven upgrade rolls, two relic rarities");

            // The other places: the game's own things where the arena holds them, else the next built ones by the house rule.
            string[] gameColorless = ["Discovery", "Calamity"];
            var colorless = stock["colorless"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()).ToList();
            c.Equal(2, colorless.Count, "two colorless cards: the game never shows an empty place");
            c.Sequence(["Uncommon", "Rare"], stock["colorless"]!.AsArray().Select(card => card!["rarity"]!.GetValue<string>()), "an Uncommon one, then a Rare one");
            var cardsLogged = LoggedOn(run, 5, "card");
            // A pick that is drawn again takes one more number of shops, so everything after it on that stream is the arena's
            // own: the Rare card is the game's Calamity only while the Uncommon pick before it was not drawn again.
            if (!CardPools.IsBuilt(gameColorless[0]))
                c.Equal(gameColorless[0], cardsLogged.FirstOrDefault(), $"the game's Uncommon colorless card, Discovery, is not built: it is the first card the shelf passed over ({string.Join(", ", cardsLogged)})");
            else
            {
                c.Equal(Content.Registry.Card(gameColorless[0]).Name, colorless[0], "the Uncommon place holds the game's Discovery");
                if (CardPools.IsBuilt(gameColorless[1])) c.Equal(Content.Registry.Card(gameColorless[1]).Name, colorless[1], "and the Rare place the game's Calamity");
                else c.Equal(gameColorless[1], cardsLogged.FirstOrDefault(), "the game's Rare colorless card, Calamity, is not built: it is the first card the shelf passed over");
            }
            c.That(cardsLogged.All(card => !CardPools.IsBuilt(card)), "nothing built was passed over");
            var shelf = stock["relics"]!.AsArray().Select(entry => entry!["name"]!.GetValue<string>()).ToList();
            var expected = ShopRelicBacks.Select(FirstBuilt).ToList();
            c.Sequence(expected.Select(place => TitleOfRelic(place.Given)), shelf, "the three relics: for each place the first built relic from the back of the saved list (Paper Phrog, Pendulum, Ringing Triangle once they are built)");
            c.Sequence(expected.SelectMany(place => place.PassedOver), LoggedOn(run, 5, "relic"), "and the game's own relics that were passed over are in the log, place by place");
            c.Equal(3, stock["potions"]!.AsArray().Count, "three potions");
            c.Equal("""{"price":75,"affordable":true}""", Text(stock["removal"]), "the card removal at 75 gold");

            // The purchase of the record: Tear Asunder for 77 of the 120 gold.
            Do(run, "open");
            s = run.Snapshot();
            c.Equal("Buy Tear Asunder for 77 gold (on sale)", s.Actions.Single(action => action.Id == "buy:card0").Label, "the buy as the mod labels it");
            c.That(Ids(s).Contains(" close", StringComparison.Ordinal) && !Ids(s).Contains("proceed", StringComparison.Ordinal), "with the inventory open: buys and close, no proceed");
            Do(run, "buy:card0");
            c.Equal(43, run.Gold, "120 - 77: the record's gold after the shop");
            c.Equal("TearAsunder", run.State.Deck[^1].Def.ClassName, "Tear Asunder is the deck's last card");
            c.Equal(15, run.State.Deck.Count, "fifteen cards");
            s = run.Snapshot();
            c.Equal("1 2 3 4", string.Join(" ", s.State["stock"]!["cards"]!.AsArray().Select(card => card!["n"]!.GetValue<int>())), "the cards left keep their numbers");
            c.Equal(40, RewardsCounter(run), "buying draws nothing");
            Do(run, "close", "proceed");

            // Floor 6: the map again, and the hallway list's third fight with the line-up of the game's record.
            s = run.Snapshot();
            c.Equal("map", s.Screen, "leaving the shop opens the map");
            c.Equal("(6,4) shop", s.State["at"]!.GetValue<string>(), "standing where the shop was");
            c.Sequence(["ShrinkerBeetleWeak", "FuzzyWurmCrawlerWeak"], ((Floors)run.Director).Encounters, "the fights met so far, in the order of the game's record");
            c.Equal("(3,0) (5,1) (6,2) (6,3) (6,4)", string.Join(" ", run.State.Part<MapState>().Visited), "the path of the save");
            c.Sequence([RoomKind.Event, RoomKind.Monster, RoomKind.Monster, RoomKind.Event, RoomKind.Shop], run.State.Part<MapState>().History.Select(visit => visit.Rooms[0]), "and the room of each point");
            var monsterAhead = s.State["next"]!.AsArray().Select((option, n) => (Type: option!["type"]!.GetValue<string>(), n)).Where(option => option.Type == "monster").ToList();
            c.That(monsterAhead.Count > 0, $"a Monster point is next ({NextPlaces(s)})");
            if (monsterAhead.Count > 0)
            {
                Do(run, $"map:{monsterAhead[0].n}");
                c.Equal(6, run.State.TotalFloor, "on floor 6");
                c.Equal("SlimesWeak", run.Fight!.EncounterName, "the third entry of the hallway list, as the record has it");
                c.Sequence(["Twig Slime (S)", "Twig Slime (M)", "Leaf Slime (S)"], run.Fight.Enemies.Select(enemy => enemy.Name),
                    "its line-up, drawn on the encounter's own stream of floor 6: the three monsters of the record, in its order");
            }

            // The save is the run: map choices, every card played in the fights, the rewards taken.
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new Floors(), run.Actions).Dump(), "the whole run replays from its action list");
            c.Equal(run.Dump(), run.Clone().Dump(), "and a copy is in the same state");
        });
    }

    private static void StrictRun(Check c)
    {
        c.Group("the same run under the game's own streams with nothing drawn again: it is the game's run until something is not built", () =>
        {
            var setup = new RunSetup { Seed = PlayedSeed };
            var run = new Arena.Run(setup, new Floors { Strict = true });
            Do(run, "option:1", "option:0", "map:2");
            PlayFight(run);
            var offer = Offered(TakeGoldAndOpenCards(run));
            Do(run, $"card:{offer.IndexOf("Colossus")}", "proceed", "map:0");
            PlayFight(run);
            Do(run, "reward:1");
            offer = Offered(TakeGoldAndOpenCards(run));
            Do(run, $"card:{offer.IndexOf("Fight Me!")}", "proceed", "map:0");
            c.That(!run.IsOver && run.State.TotalFloor == 4 && RewardsCounter(run) == 25, "everything up to the event's room is built: the run stands on floor 4 with rewards at 25");

            var before = run.Clone();
            Do(run, "option:1");
            if (RunRelics.IsBuilt("StoneCracker"))
            {
                c.That(!run.IsOver && run.State.HasRelic("StoneCracker"), "Stone Cracker is built: the run has the game's relic and goes on");
                return;
            }

            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "Stone Cracker is not built: the run ends as unrepresentable, by itself");
            c.That(run.ResultReason is { } reason && reason.Contains("StoneCracker", StringComparison.Ordinal), $"and the reason names the relic ({run.ResultReason})");
            c.Equal(4, run.State.TotalFloor, "on floor 4");
            c.Equal(25, RewardsCounter(run), "standing as before the action: the rarity's number is not drawn");
            c.Equal(before.State.Dump().ToJsonString(), run.State.Dump().ToJsonString(), "nothing of the state changed");
            c.Equal("option:1", run.Actions[^1], "the action stays in the save");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new Floors { Strict = true }, run.Actions).Dump(), "which replays to the same end");
        });
    }

    // ---------- the parts name the same things ----------

    /// <summary>
    /// Lists that different parts of run mode keep of the same things of the game, held against each other: each was
    /// read from the game's code by another reader.
    /// </summary>
    private static void TheListsAgree(Check c)
    {
        c.Group("the lists that different parts keep of the same things agree", () =>
        {
            // Neow's relics: the event's own lists (Neow.cs) and the list of reachable relics (ReachableRelics.cs).
            // Massive Scroll is offered only in a run of more than one player, so a single-player run cannot get it.
            c.Sequence(NeowRelics.All.Where(relic => relic != "MassiveScroll").Order(StringComparer.Ordinal),
                ReachableRelics.From(RelicSource.Neow).Select(relic => relic.ClassName).Order(StringComparer.Ordinal),
                "the 27 relics Neow can offer in single player are the reachable relics whose source is Neow");
            c.That(!NeowRelics.IsAllowed("MassiveScroll", RunUnlocks.All) && NeowRelics.IsAllowed("MassiveScroll", RunUnlocks.All, players: 2),
                "Massive Scroll is the one that needs a second player");

            // The cards Neow's relics and the built rooms hand out by class name are built (curses, Neow's Fury).
            foreach (var card in new[] { "Greed", "Injury", "NeowsFury", "Clumsy", "PoorSleep", "Guilty", "SporeMind" })
                c.That(CardPools.IsBuilt(card), $"{card} is built, for the relic or event that gives it");

            // What a shop needs so that no place of its shelf is empty (plan 5.1): built colorless cards of both rarities, and a
            // built Shop-rarity relic that a shop may sell.
            foreach (var rarity in ShopInventory.ColorlessCardRarities)
                c.That(CardPools.Colorless.Unlocked(RunUnlocks.All).Any(card => card.Rarity == rarity && CardPools.IsBuilt(card.ClassName)), $"a built {rarity} colorless card exists for the shop's shelf");
            c.That(Relics1.Shop.Any(relic => !RunRelics.NeverInShops.Contains(relic) && RunRelics.IsBuilt(relic)), "and a built relic of Shop rarity that a shop may sell");

            // The run's hooks: every one of the 27 is declared and names a call site; the three this wave declared name the room that sends them.
            c.That(RunHooks.Hooks.All(hook => hook.IsDeclared), "none of the 27 hooks outside a fight waits for a type any more");
            c.Equal("RestSite.GenerateOptions", RunHooks.Hooks.Single(hook => hook.Name == "TryModifyRestSiteOptions").SentBy, "the rest site sends its options hook");
            c.Equal("ShopHooks.ModifyMerchantPrice", RunHooks.Hooks.Single(hook => hook.Name == "ModifyMerchantPrice").SentBy, "the shop sends its price hook");
            c.Equal("ShopHooks.ShouldRefillMerchantEntry", RunHooks.Hooks.Single(hook => hook.Name == "ShouldRefillMerchantEntry").SentBy, "and its refill hook");
        });
    }

    // ---------- whole acts ----------

    /// <summary>
    /// A scripted walker for every screen of an act: Greedy in the fights, and outside them plain rules that touch every
    /// kind of action (one of Neow's relics and whatever its pickup asks, every reward taken, a card of every card reward,
    /// Rest and Smith by turns, the chest opened and its relic taken, up to two purchases in a shop, an option of every
    /// event page). Which of several equal choices it takes depends on the step count alone, so a walk is the same every time.
    /// </summary>
    private static string Walk(Arena.Run run, GameSnapshot s, int step, ref (int Floor, int Bought) shop)
    {
        var ids = s.Actions.Select(action => action.Id).ToList();
        string? First(string prefix) => ids.FirstOrDefault(id => id.StartsWith(prefix, StringComparison.Ordinal));
        List<string> All(string prefix) => ids.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        string Pick(List<string> among) => among[step % among.Count];

        if (run.Fight is not null) return Greedy.Choose(s);
        switch (s.Screen)
        {
            case "map":
                return Pick(All("map:"));
            case "rewards":
                return First("reward:") ?? (ids.Contains("proceed") ? "proceed" : ids[0]);
            case "card_reward":
                return Pick(All("card:"));
            case "card_select":
                // A deck screen: pick a card that is not picked yet, then confirm the preview. A row to choose from: take one.
                if (ids.Contains("confirm")) return "confirm";
                var unpicked = s.Actions.Where(action => action.Id.StartsWith("card:", StringComparison.Ordinal) && !action.Label.StartsWith("Deselect", StringComparison.Ordinal)).Select(action => action.Id).ToList();
                return unpicked.Count > 0 ? Pick(unpicked) : ids[0];
            case "rest":
                return All("option:") is { Count: > 0 } options ? Pick(options) : "proceed";
            case "treasure":
                return ids.Contains("open") ? "open" : First("relic:") ?? "proceed";
            case "shop":
                if (shop.Floor != run.State.TotalFloor) shop = (run.State.TotalFloor, 0);
                if (ids.Contains("open") && shop.Bought < 2) return "open";
                if (ids.Contains("close"))
                {
                    var buys = All("buy:");
                    if (buys.Count == 0 || shop.Bought >= 2)
                    {
                        shop.Bought = 2;
                        return "close";
                    }
                    shop.Bought++;
                    return Pick(buys);
                }
                return "proceed";
            case "event":
                // Neow's room (one of her three relics, then Leave) or an event of the act's list: one of the options the
                // page offers (an option that would kill is listed in the state and not offered).
                return Pick(All("option:"));
            case "game_over":
                return "continue";
            default:
                return ids[0];
        }
    }

    /// <summary>A run's result and state without the two logs that only the act's own director writes in full.</summary>
    private static string WithoutLogs(Arena.Run run)
    {
        var state = run.State.Dump();
        var parts = state["parts"]!.AsObject();
        parts.Remove(nameof(FloorLog));
        parts.Remove(nameof(LeftOutLog));
        return $"{run.Result} {state.ToJsonString()}";
    }

    /// <summary>Where two dumps part, with a little of both around it.</summary>
    private static string Difference(string a, string b)
    {
        var at = 0;
        while (at < a.Length && at < b.Length && a[at] == b[at]) at++;
        static string Around(string text, int at) => text[Math.Max(0, at - 160)..Math.Min(text.Length, at + 120)];
        return $" (from character {at}: [{Around(a, at)}] against [{Around(b, at)}])";
    }

    private static void WholeActs(Check c)
    {
        c.Group("whole acts walked through every room that is built: the rooms fit each other from Neow's room to the boss", () =>
        {
            // Seeds whose own act roll gives Overgrowth, as a run of the game would have them. $ARENA_RUN_SOAK asks for
            // more acts than the 24 of every self-check (a few hundred take a minute).
            var acts = int.TryParse(Environment.GetEnvironmentVariable("ARENA_RUN_SOAK"), out var asked) && asked > 0 ? asked : 24;
            var seedMaker = new GameRng(20261011);
            var seeds = new List<string>();
            while (seeds.Count < acts)
            {
                var seed = RunSeed.Random(seedMaker);
                if (RunGeneration.RollActs(seed, RunUnlocks.All)[0] == GameLists.Overgrowth) seeds.Add(seed.Text);
            }

            var results = new Dictionary<string, int>(StringComparer.Ordinal);
            var screens = new HashSet<string>(StringComparer.Ordinal);
            var rooms = new HashSet<RoomKind>();
            var problems = new List<string>();
            var floors = 0;
            var compared = 0;
            foreach (var seed in seeds)
            {
                var setup = new RunSetup { Seed = seed };
                var run = new Arena.Run(setup, new Floors());
                (int Floor, int Bought) shop = (0, 0);
                Arena.Run? halfway = null;
                var halfwayAt = 0;
                for (var step = 0; step < 20000 && !run.AtEnd; step++)
                {
                    var s = run.Snapshot();
                    screens.Add(s.Screen);
                    var id = Walk(run, s, step, ref shop);
                    var (relicsBefore, cardsBefore, goldBefore, potionsBefore) = (run.Relics.Count, run.Deck.Count, run.Gold, run.Potions.Count(potion => potion is not null));
                    var result = run.Execute(id);
                    if (!result.Ok)
                    {
                        problems.Add($"{seed}: '{id}' was refused on {s.Screen} at floor {run.State.TotalFloor}: {result.Error}");
                        break;
                    }
                    // What was taken is held afterwards, and what was bought was paid for.
                    bool Is(string prefix) => id.StartsWith(prefix, StringComparison.Ordinal);
                    if (run.Fight is null && !run.IsOver)
                    {
                        if ((Is("buy:relic") || (Is("relic:") && s.Screen == "treasure")) && run.Relics.Count != relicsBefore + 1) problems.Add($"{seed}: after {id} on floor {run.State.TotalFloor} the player holds {run.Relics.Count} relics, not {relicsBefore + 1}");
                        if ((Is("buy:card") || Is("buy:colorless")) && run.Deck.Count != cardsBefore + 1) problems.Add($"{seed}: after {id} on floor {run.State.TotalFloor} the deck has {run.Deck.Count} cards, not {cardsBefore + 1}");
                        if (Is("buy:potion") && run.Potions.Count(potion => potion is not null) != potionsBefore + 1) problems.Add($"{seed}: after {id} on floor {run.State.TotalFloor} the belt does not hold one potion more");
                        if (Is("buy:") && id != "buy:remove" && run.Gold >= goldBefore) problems.Add($"{seed}: {id} on floor {run.State.TotalFloor} cost nothing ({goldBefore} gold before, {run.Gold} after)");
                        // A chest with a relic in it pays its gold on opening (an empty one, Silver Crucible's, pays nothing).
                        if (id == "open" && s.Screen == "treasure" && run.Snapshot().State["relics"] is not null && run.Gold < goldBefore + TreasureRoom.MinGold)
                            problems.Add($"{seed}: the chest of floor {run.State.TotalFloor} paid {run.Gold - goldBefore} gold");
                    }
                    if (run.Hp < 0 || run.Hp > run.MaxHp || run.Gold < 0) problems.Add($"{seed}: {run.Hp}/{run.MaxHp} HP, {run.Gold} gold at floor {run.State.TotalFloor}");
                    // A copy taken outside a fight in the middle of the act, to be played on from there.
                    if (halfway is null && run.Fight is null && run.State.TotalFloor >= 8)
                    {
                        halfway = run.Clone();
                        halfwayAt = run.Actions.Count;
                    }
                }

                if (!run.AtEnd) problems.Add($"{seed}: the walk did not reach the end of the run (floor {run.State.TotalFloor}, {run.Snapshot().Screen})");
                var how = run.Result ?? "unfinished";
                results[how] = results.GetValueOrDefault(how) + 1;
                floors += run.State.TotalFloor;
                foreach (var visit in run.State.Part<MapState>().History) rooms.UnionWith(visit.Rooms);
                if (run.Result == RunResult.Error || run.Result == RunResult.Unrepresentable) problems.Add($"{seed}: ended as {run.Result}: {run.ResultReason}");
                if (run.Result == RunResult.Cleared && run.State.TotalFloor != 17) problems.Add($"{seed}: cleared on floor {run.State.TotalFloor}, not on the boss's floor 17");
                if (run.State.Part<MapState>().History.Count != run.State.TotalFloor) problems.Add($"{seed}: {run.State.Part<MapState>().History.Count} points visited on floor {run.State.TotalFloor}");
                if (run.State.Relics.Select(relic => relic.ClassName).Distinct().Count() != run.State.Relics.Count) problems.Add($"{seed}: a relic is held twice ({string.Join(", ", run.State.Relics.Select(relic => relic.ClassName))})");
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
                    if (built) problems.Add($"{seed}: the log says {entry.Kind} {entry.ClassName} was passed over on floor {entry.Floor}, but it is built");
                }

                // The save is the run, and a copy taken on the way plays on to the same end.
                var replayed = Arena.Run.Replay(setup, new Floors(), run.Actions).Dump();
                if (run.Dump() != replayed) problems.Add($"{seed}: the replay of the action list ends in another state{Difference(run.Dump(), replayed)}");

                // The act's own director gives the same run for the same actions: the two were written apart and hold each
                // other. (Only the logs differ: the floor log is that director's, and it also writes an Unknown point's second
                // roll to the run's log.)
                try
                {
                    var byTheAct = ActRun.Replay(seed, RunMode.DrawAgain, run.Actions);
                    if (WithoutLogs(byTheAct) != WithoutLogs(run)) problems.Add($"{seed}: the act's director ends the same actions in another state{Difference(WithoutLogs(run), WithoutLogs(byTheAct))}");
                    compared++;
                }
                catch (InvalidOperationException refused)
                {
                    problems.Add($"{seed}: the act's director turns down an action of this walk: {refused.Message}");
                }
                if (halfway is not null)
                {
                    foreach (var id in run.Actions.Skip(halfwayAt)) halfway.Execute(id);
                    if (halfway.Dump() != run.Dump()) problems.Add($"{seed}: a copy taken at action {halfwayAt} and played on ends in another state");
                }
            }

            c.Equal("", string.Join(" | ", problems.Take(6)), $"{seeds.Count} acts: every action the walker chose was accepted, no run ended in an error or as unrepresentable, every run replays and copies, and the act's own director ends the same actions in the same state ({problems.Count} problems)");
            c.Equal(seeds.Count, compared, "every act was played again through the act's own director, action for action");
            c.Equal(seeds.Count, results.GetValueOrDefault(RunResult.Cleared) + results.GetValueOrDefault(RunResult.Died), $"every act ended cleared or with a death ({string.Join(", ", results.OrderBy(r => r.Key).Select(r => $"{r.Key} {r.Value}"))})");
            c.That(results.GetValueOrDefault(RunResult.Cleared) > 0, "some acts were cleared: the boss was beaten and its rewards' proceed ended the act");
            foreach (var screen in new[] { "map", "combat", "rewards", "card_reward", "card_select", "rest", "treasure", "shop", "event", "game_over" })
                c.That(screens.Contains(screen), $"the walks met the screen {screen}");
            foreach (var room in new[] { RoomKind.Monster, RoomKind.Elite, RoomKind.Boss, RoomKind.Treasure, RoomKind.Shop, RoomKind.Event, RoomKind.RestSite })
                c.That(rooms.Contains(room), $"and a {room} room");
            c.That(floors > seeds.Count * 8, $"the walks went deep: {floors} floors in {seeds.Count} acts");
        });
    }
}
