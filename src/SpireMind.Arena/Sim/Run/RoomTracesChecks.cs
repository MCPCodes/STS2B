using System.Globalization;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// A second reader's checks of what a room rolls: the rewards of a fight, a shop's shelf and
/// prices, a chest, a rest site. Thirty rooms of three runs (twenty fights, four shops, three
/// rest sites, three chests) are entered through the rooms' own entry points and held, number
/// for number, to <see cref="RoomTraces"/>: what an independent script made of the same seeds
/// by following the game's code (<c>Sim/Run/Tools/trace_rooms.py</c>; the page next to this
/// file, <c>ROOM-TRACES.md</c>, says how to follow a room by hand).
/// <para>
/// What a room is held to: how many numbers each of the three streams had given before and
/// after; a fight's gold, potion, three cards with the rarity each was rolled at, the order
/// of its rows, the pity offset and the potion chance afterwards, an elite's relic, the extra
/// number a gold row draws that the set was made with; a shop's card on sale, its seven cards,
/// three relics and three potions with all thirteen prices and the removal's; a chest's rolled
/// rarity, its relic and its gold, the one number on entering and the one on opening; that a
/// rest draws nothing. And what the player then does goes through the screens: the gold is
/// taken, the card reward opened and its first card taken, the chest opened and its relic
/// taken.
/// </para>
/// <para>
/// Relics. The trace names the relic the GAME gives. Most of those are not built, and the
/// arena then shows the next built relic of the same list and notes in the run's log what it
/// passed over (a house rule; it draws nothing). The checks work both out from the trace's
/// relic bags, which are the game's lists in the game's order: one copy of the bags is pulled
/// from as the game pulls (and must give the trace's relics), another as the arena pulls (the
/// first relic the registry holds), and the run must show that one and log the ones before
/// it. Only "is this relic built" is asked of the arena. So the checks keep holding when more
/// relics are built: the shown relic moves towards the game's own, and nothing else changes,
/// because the player of these runs takes no relic before the chest that ends each run.
/// </para>
/// <para>
/// A real run. <see cref="RoomTraces.Recorded"/> is the game's own record of the winning run on
/// seed 75F277YN2G, floors 2 to 14, typed in by hand: nine fights, a shop and a chest, walked
/// through the same entry points with the record's player taking what he took.
/// </para>
/// <para>
/// Prices. A price is a table value times a rolled factor in 32-bit floats, a half going to
/// the even number. <see cref="RoomTraces.PriceTies"/> are shops in which that matters: the
/// product of the first card's, relic's or potion's price lies exactly on a half as a float,
/// so half-up rounding or 64-bit arithmetic would give another price.
/// </para>
/// <para>
/// Thresholds. The game tests a potion drop and a card's rarity with "below", a potion's
/// rarity with "at or below", a relic's rarity with "below" and the upgrade roll with "at or
/// below". Each is held by a roll that lies exactly on its threshold: by setting the run's
/// odds to the roll (<see cref="RoomTraces.Edges"/>), by a place of a stream where the number
/// is the fixed threshold itself (<see cref="RoomTraces.ExactRolls"/>), or by a check relic
/// that sets the upgrade chance to a card's own roll.
/// </para>
/// </summary>
public sealed class RoomTracesChecks : IArenaChecks
{
    public void Run(Check c)
    {
        TheGamesLists(c);
        foreach (var trace in RoomTraces.Runs) Walk(c, trace);
        foreach (var record in RoomTraces.Recorded) Recorded(c, record);
        Refills(c);
        PriceTies(c);
        Edges(c);
        ExactRolls(c);
        TheUpgradeRoll(c);
        ThePotionChance(c);
        TheRestsNumber(c);
    }

    // ---------- the director of the checks ----------

    /// <summary>
    /// A run that is nothing but rooms, one after the other, each opened through its entry point:
    /// a fight's rewards (<see cref="RunRewards.OfferForRoomEnd(Arena.Run, RewardRoom)"/>; the fight
    /// itself draws from other streams and is left out), a shop, a rest site, a chest. Leaving a
    /// room opens the next one; after the last the run is over.
    /// </summary>
    private sealed class Rooms : RunDirector
    {
        public string[] Script = [];
        public int Next;

        /// <summary>The total floor of the first room: 2, behind the Ancient's.</summary>
        public int FirstFloor = 2;

        /// <summary>Where the two streams of the player and the run's treasure stream are moved on to before the first room; 0 leaves them.</summary>
        public int RewardsAt, ShopsAt, TreasureAt;

        /// <summary>The pity offset and the potion chance the run starts with, when it starts in the middle of a recorded run.</summary>
        public float? Offset, Chance;

        /// <summary>Relics that have left both bags before the first room.</summary>
        public string[] Gone = [];

        public override void Begin(Arena.Run run)
        {
            var world = RunWorld.Begin(run.State);
            world.Streams.FastForward(RunStream.Rewards, RewardsAt);
            world.Streams.FastForward(RunStream.Shops, ShopsAt);
            world.Streams.FastForward(RunStream.TreasureRoomRelics, TreasureAt);
            if (Offset is { } offset) run.State.CardRarityOffset = offset;
            if (Chance is { } chance) run.State.PotionChance = chance;
            foreach (var relic in Gone) RunRelics.TakeOutOfBags(run.State, relic);
            Open(run);
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => throw new NotSupportedException("No fight is played in these runs.");

        public override void AfterRoom(Arena.Run run) => Open(run);

        private void Open(Arena.Run run)
        {
            if (Next >= Script.Length)
            {
                run.End(RunResult.Cleared);
                return;
            }
            var room = Script[Next++];
            var state = run.State;
            state.TotalFloor = FirstFloor + Next - 1;
            state.ActFloor = state.TotalFloor;
            state.Part<RunWorld>().Streams.MoveTo(0, state.TotalFloor - 1, 3);
            switch (room[0])
            {
                case 'M' or 'E' or 'B':
                    var rewards = RewardRoom.Combat(room[0] switch { 'M' => RoomType.Monster, 'E' => RoomType.Elite, _ => RoomType.Boss });
                    // Gold a thief took and the fight won back: a row the set is made with (HeistPower.cs). A record does not say how much.
                    if (room.Length > 1)
                    {
                        var stolen = room.Length > 2 ? int.Parse(room[2..], CultureInfo.InvariantCulture) : 5;
                        rewards = rewards with { ExtraRewards = [new GoldReward(stolen, wasGoldStolenBack: true)] };
                    }
                    if (RunRewards.OfferForRoomEnd(run, rewards) is null) throw new InvalidOperationException("A fight's room has a rewards screen.");
                    break;
                case 'S':
                    ShopRoom.Enter(run);
                    break;
                case 'R':
                    RestSite.Enter(run);
                    break;
                case 'T':
                    TreasureRoom.Enter(run);
                    break;
                default:
                    throw new ArgumentException($"'{room}' is no room of a trace.");
            }
        }
    }

    /// <summary>The total floor of a traced run's room: the first room stands on floor 2, behind the Ancient's.</summary>
    private static int FloorOf(int room) => room + 2;

    // ---------- the relic bags, pulled from by rule ----------

    /// <summary>
    /// The two relic bags of a run as lists of names, with the rules of a pull written out: an
    /// elite's relic is the first of its list of the player's bag, a shop's the last one a shop
    /// sells, a chest's the first of its list of the shared bag; what is pulled from the player's
    /// bag leaves the shared bag too; a chest's relic leaves the player's bag when it is taken.
    /// <paramref name="holds"/> says which relics can be given: everything for the game, the
    /// built ones for the arena, which passes over the others in pull order.
    /// </summary>
    private sealed class Bags(RoomTraces.Trace trace, Func<string, bool> holds)
    {
        private static readonly RelicRarity[] Rarities = [RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop];

        private readonly Dictionary<RelicRarity, List<string>> _player = Rarities.ToDictionary(r => r, r => trace.Player.Of(r).ToList());
        private readonly Dictionary<RelicRarity, List<string>> _shared = Rarities.Take(3).ToDictionary(r => r, r => trace.Shared.Of(r).ToList());

        public (string Given, List<string> PassedOver) FromThePlayersFront(RelicRarity rarity) => Pull(_player[rarity], _player[rarity], leavesShared: true);

        public (string Given, List<string> PassedOver) FromThePlayersBack(RelicRarity rarity) =>
            Pull(_player[rarity], Enumerable.Reverse(_player[rarity]).Where(relic => !RoomTraces.NeverSold.Contains(relic)), leavesShared: true);

        public (string Given, List<string> PassedOver) FromTheSharedFront(RelicRarity rarity) => Pull(_shared[rarity], _shared[rarity], leavesShared: false);

        /// <summary>The relic was obtained: it is in no bag any more.</summary>
        public void Obtained(string relic)
        {
            foreach (var list in _player.Values.Concat(_shared.Values)) list.Remove(relic);
        }

        private (string Given, List<string> PassedOver) Pull(List<string> list, IEnumerable<string> inPullOrder, bool leavesShared)
        {
            var order = inPullOrder.ToList();
            var given = order.First(holds);
            list.Remove(given);
            if (leavesShared)
                foreach (var shared in _shared.Values) shared.Remove(given);
            return (given, order.TakeWhile(relic => relic != given).ToList());
        }
    }

    // ---------- helpers ----------

    private static RunWorld World(Arena.Run run) => run.State.Part<RunWorld>();

    private static RoomTraces.Counters Counters(Arena.Run run)
    {
        var streams = World(run).Streams;
        return new(streams.Counter(RunStream.Rewards), streams.Counter(RunStream.Shops), streams.Counter(RunStream.TreasureRoomRelics));
    }

    /// <summary>The relics the run's log holds as passed over on a floor, in order. (FindPart: asking with Part would make a log a replay does not have.)</summary>
    private static List<string> PassedOverOn(Arena.Run run, int floor) =>
        (run.State.FindPart<LeftOutLog>()?.Entries ?? []).Where(entry => entry.Floor == floor && entry.Kind == "relic").Select(entry => entry.ClassName).ToList();

    private static RelicRarity Rarity(string name) => Enum.Parse<RelicRarity>(name);

    private static string TitleOfCard(string className) => Content.Registry.Card(className).Name;

    /// <summary>A reward row in one word and its content by class name.</summary>
    private static string Row(Reward reward) => reward switch
    {
        GoldReward gold => string.Create(CultureInfo.InvariantCulture, $"gold {gold.Amount}{(gold.WasGoldStolenBack ? " stolen back" : "")}"),
        PotionReward potion => $"potion {potion.Potion?.ClassName}",
        RelicReward relic => $"relic {relic.Relic?.ClassName}",
        CardReward cards => "cards " + string.Join(" ", cards.Cards.Select(card => card.Def.ClassName + (card.IsUpgraded ? "+" : ""))),
        _ => reward.Kind,
    };

    // ---------- the lists the trace was made from ----------

    private static void TheGamesLists(Check c)
    {
        c.Group("a second reading of the game's class files agrees with the lists the arena pulls and sells from", () =>
        {
            c.Sequence(RoomTraces.NeverSold, RunRelics.NeverInShops.Order(StringComparer.Ordinal), "the relics a shop never sells");
            c.Equal(3, RoomTraces.Runs.Length, "three traced runs");
            c.Equal(20, RoomTraces.Runs.Sum(run => run.Rooms.OfType<RoomTraces.FightRoom>().Count()), "twenty fights among their rooms");
            c.Equal(4, RoomTraces.Runs.Sum(run => run.Rooms.OfType<RoomTraces.ShopRoomTrace>().Count()), "four shops");
            c.Equal(3, RoomTraces.Runs.Sum(run => run.Rooms.OfType<RoomTraces.ChestRoom>().Count()), "three chests");
            c.Equal(3, RoomTraces.Runs.Sum(run => run.Rooms.OfType<RoomTraces.RestRoom>().Count()), "three rest sites");
        });
    }

    // ---------- a traced run ----------

    private static void Walk(Check c, RoomTraces.Trace trace)
    {
        var script = trace.Script.Split(',');
        var setup = new RunSetup { Seed = trace.Seed };
        Arena.Run? walked = null;

        c.Group($"seed {trace.Seed}: the run starts as the second reader has it", () =>
        {
            var run = walked = new Arena.Run(setup, new Rooms { Script = script });
            var world = World(run);
            c.Equal("Overgrowth", world.Acts[0], "the first act");
            c.Equal(script.Length, trace.Rooms.Length, "a traced room for every room of the script");
            // The lists are compared in a run of their own, whose first room pulls nothing.
            var fresh = new Arena.Run(setup, new Rooms { Script = ["R"] });
            foreach (var rarity in new[] { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop })
            {
                c.Sequence(trace.Player.Of(rarity), World(fresh).PlayerBag.Relics(rarity), $"the player's bag, {rarity}: the game's order out of the seed");
                if (rarity != RelicRarity.Shop)
                    c.Sequence(trace.Shared.Of(rarity), World(fresh).SharedBag.Relics(rarity), $"the shared bag, {rarity}");
            }
        });
        if (walked is null) return;

        var theRun = walked;
        var game = new Bags(trace, static _ => true);
        var arena = new Bags(trace, RunRelics.IsBuilt);
        var gold = RunSetup.IroncladGold;
        var stopped = false;

        for (var n = 0; n < script.Length && !stopped; n++)
        {
            var index = n;
            var room = trace.Rooms[n];
            var before = c.Failures.Count;
            c.Group($"seed {trace.Seed}, room {n + 1} of {script.Length} ({script[n]}, floor {FloorOf(n)})", () =>
            {
                var run = theRun;
                c.Equal(FloorOf(index), run.State.TotalFloor, "the floor");
                switch (room)
                {
                    case RoomTraces.FightRoom fight:
                        Fight(c, run, fight, FloorOf(index), game, arena, ref gold);
                        break;
                    case RoomTraces.ShopRoomTrace shop:
                        Shop(c, run, shop, FloorOf(index), game, arena);
                        break;
                    case RoomTraces.RestRoom rest:
                        Rest(c, run, rest);
                        break;
                    case RoomTraces.ChestRoom chest:
                        Chest(c, run, chest, FloorOf(index), game, arena, ref gold);
                        break;
                }
                c.Equal(gold, run.Gold, "the gold after the room");
            });
            // A room that failed leaves the run somewhere else: the rooms behind it would only repeat the failure.
            stopped = c.Failures.Count > before;
        }
        if (stopped) return;

        c.Group($"seed {trace.Seed}: the whole run", () =>
        {
            var run = theRun;
            c.Equal(RunResult.Cleared, run.Result, "the run is over after its last room");
            c.That((run.State.FindPart<LeftOutLog>()?.Entries ?? []).All(entry => entry.Kind == "relic"),
                "nothing but relics was passed over: every card and potion the rooms rolled is the game's own");
            c.Equal(RunSetup.IroncladDeck.Count + trace.Rooms.OfType<RoomTraces.FightRoom>().Count(), run.State.Deck.Count, "one card was taken after every fight");
            var replayed = Arena.Run.Replay(setup, new Rooms { Script = script }, run.Actions);
            c.Equal(run.Dump(), replayed.Dump(), "the run again from its list of actions is the same run");
        });
    }

    private static void Fight(Check c, Arena.Run run, RoomTraces.FightRoom room, int floor, Bags game, Bags arena, ref int gold)
    {
        c.That(run.Top is RewardsScreen, "the room's rewards screen is up");
        var screen = (RewardsScreen)run.Top!;
        c.Equal(room.After, Counters(run), "everything is rolled when the screen comes up: rewards, shops and treasure_room_relics stand at");
        c.Equal(11 + (room.Potion is null ? 0 : 2) + (room.Kind == "E" ? 1 : 0) + (room.Stolen > 0 ? 1 : 0), room.After.Rewards - room.Before.Rewards,
            "numbers of rewards: the potion roll, the gold, three for each card; two for a potion, one for an elite's relic, one for a gold row the set was made with");

        // The rows in the screen's order: gold, potion, relic, card.
        var rows = new List<string> { string.Create(CultureInfo.InvariantCulture, $"gold {room.Gold}") };
        if (room.Stolen > 0) rows.Add(string.Create(CultureInfo.InvariantCulture, $"gold {room.Stolen} stolen back"));
        if (room.Potion is not null) rows.Add($"potion {room.Potion}");
        if (room.Relic is not null)
        {
            var rarity = Rarity(room.RelicRarity!);
            c.Equal(room.Relic, game.FromThePlayersFront(rarity).Given, $"the game's relic is the front of the {rarity} list of the player's bag as the game has it");
            var (shown, passedOver) = arena.FromThePlayersFront(rarity);
            rows.Add($"relic {shown}");
            c.Sequence(passedOver, PassedOverOn(run, floor), "the run's log names what the pull passed over, in pull order, and nothing else");
        }
        else
        {
            c.Equal(0, PassedOverOn(run, floor).Count, "no relic, nothing passed over");
        }
        rows.Add("cards " + string.Join(" ", room.Cards));
        c.Sequence(rows, screen.Set.Rewards.Select(Row), "the rows");
        c.Sequence(room.Rarities, screen.Set.Rewards.OfType<CardReward>().Single().Cards.Select(card => card.Def.Rarity.ToString()), "the rarity of each card");
        c.Equal(room.Offset, run.State.CardRarityOffset, "the pity offset after the three cards");
        c.Equal(room.Chance, run.State.PotionChance, "the potion chance after the roll");

        // Through the screens: the gold, then the card reward and its first card.
        Do(run, "reward:0");
        gold += room.Gold;
        if (room.Stolen > 0)
        {
            Do(run, "reward:0");
            gold += room.Stolen;
        }
        var left = run.Snapshot().State["rewards"]!.AsArray();
        c.Equal(rows.Count - (room.Stolen > 0 ? 2 : 1), left.Count, "the rows that are left");
        Do(run, string.Create(CultureInfo.InvariantCulture, $"reward:{left.Count - 1}"));
        var offer = run.Snapshot();
        c.Equal("card_reward", offer.Screen, "the last row opens the card reward");
        c.Sequence(room.Cards.Select(TitleOfCard), offer.State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()), "the cards on its screen");
        Do(run, "card:0");
        c.Equal(room.Cards[0], run.State.Deck[^1].Def.ClassName, "the first card is the deck's last now");
        c.Equal(room.After, Counters(run), "taking rewards draws nothing");
        Do(run, "proceed");
    }

    private static void Shop(Check c, Arena.Run run, RoomTraces.ShopRoomTrace room, int floor, Bags game, Bags arena)
    {
        c.That(run.Top is ShopScreen, "the shop's screen is up");
        var shelf = ((ShopScreen)run.Top!).Inventory;
        c.Equal(room.After, Counters(run), "the shelf is rolled on entering: rewards, shops and treasure_room_relics stand at");
        c.Equal(14, room.After.Rewards - room.Before.Rewards, "numbers of rewards: five rarities, seven upgrade rolls, two relic rarities");
        c.Equal(28, room.After.Shops - room.Before.Shops, "numbers of shops: the sale, seven picks, eight card prices, three relic prices, six for the potions, three potion prices");

        var cards = shelf.CardEntries.ToList();
        c.Sequence(room.Cards, cards.Select(entry => entry.Card!.Def.ClassName + (entry.Card.IsUpgraded ? "+" : "")), "the five character cards and the two colorless ones");
        c.Sequence(room.CardPrices, cards.Select(entry => entry.BaseCost), "their prices");
        c.Sequence(Enumerable.Range(0, 7).Select(slot => slot == room.Sale), cards.Select(entry => entry.IsOnSale), "the one on sale");

        var shown = new List<string>();
        var passed = new List<string>();
        for (var place = 0; place < room.Relics.Length; place++)
        {
            var rarity = Rarity(room.RelicRarities[place]);
            c.Equal(room.Relics[place], game.FromThePlayersBack(rarity).Given, $"relic place {place}: the game's relic is the last one of the {rarity} list that a shop sells");
            var (given, passedOver) = arena.FromThePlayersBack(rarity);
            shown.Add(given);
            passed.AddRange(passedOver);
        }
        c.Sequence(shown, shelf.Relics.Select(entry => entry.Relic!.ClassName), "the three relics on the shelf");
        c.Sequence(room.RelicPrices, shelf.Relics.Select(entry => entry.BaseCost), "their prices (a relic shelved in place of the game's is of the same list)");
        c.Sequence(passed, PassedOverOn(run, floor), "the run's log names what the three pulls passed over, in pull order, and nothing else");

        c.Sequence(room.Potions, shelf.Potions.Select(entry => entry.Potion!.ClassName), "the three potions");
        c.Sequence(room.PotionPrices, shelf.Potions.Select(entry => entry.BaseCost), "their prices");
        c.Equal(room.Removal, shelf.Removal!.BaseCost, "the card removal");

        // What the screen says is what was rolled: nothing the player holds changes a price.
        var stock = run.Snapshot().State["stock"]!;
        c.Sequence(room.CardPrices.Take(5), stock["cards"]!.AsArray().Select(entry => entry!["price"]!.GetValue<int>()), "the screen's prices of the character cards");
        c.Sequence(room.RelicPrices, stock["relics"]!.AsArray().Select(entry => entry!["price"]!.GetValue<int>()), "the screen's prices of the relics");
        Do(run, "proceed");
    }

    private static void Rest(Check c, Arena.Run run, RoomTraces.RestRoom room)
    {
        c.That(run.Top is RestSiteScreen, "the rest site's screen is up");
        c.Equal(room.Before, Counters(run), "entering a rest site draws nothing");
        Do(run, "option:0");
        c.Sequence(["HEAL"], ((RestSiteScreen)run.Top!).ChosenIds, "Rest was taken");
        c.Equal(room.After, Counters(run), "a rest draws nothing: no relic adds rewards to it");
        Do(run, "proceed");
    }

    private static void Chest(Check c, Arena.Run run, RoomTraces.ChestRoom room, int floor, Bags game, Bags arena, ref int gold)
    {
        c.That(run.Top is TreasureScreen, "the treasure room's screen is up");
        var screen = (TreasureScreen)run.Top!;
        c.Equal(room.Entered, Counters(run), "entering: one number of treasure_room_relics, none of rewards");
        c.Equal(room.Rarity, screen.RolledRarity.ToString(), "the rolled rarity");
        var rarity = Rarity(room.Rarity);
        c.Equal(room.Relic, game.FromTheSharedFront(rarity).Given, $"the game's relic is the front of the {rarity} list of the shared bag as the game has it");
        var (shown, passedOver) = arena.FromTheSharedFront(rarity);
        c.Equal(shown, screen.Relic?.ClassName, "the relic in the chest");
        c.Equal(passedOver.Count > 0 ? passedOver[0] : shown, screen.GameRelic, "the relic the game would give from the bag as it stands");
        c.Sequence(passedOver, PassedOverOn(run, floor), "the run's log names what the pull passed over, and nothing else");

        Do(run, "open");
        screen = (TreasureScreen)run.Top!;
        c.Equal(room.After, Counters(run), "opening: one number of rewards");
        c.Equal(room.Gold, screen.Gold, "the gold the chest paid");
        gold += room.Gold;
        Do(run, "relic:0");
        c.Equal(shown, run.State.Relics[^1].ClassName, "the relic was taken");
        arena.Obtained(shown);
        c.Equal(room.After, Counters(run), "taking it draws nothing");
        Do(run, "proceed");
    }

    // ---------- the game's own record of a run ----------

    /// <summary>What the run's log holds as passed over on a floor, of one kind, in order.</summary>
    private static List<string> LoggedOn(Arena.Run run, int floor, string kind) =>
        (run.State.FindPart<LeftOutLog>()?.Entries ?? []).Where(entry => entry.Floor == floor && entry.Kind == kind).Select(entry => entry.ClassName).ToList();

    /// <summary>
    /// A relic the game gave is the one the arena gives, or, while it is not built, the first one
    /// the pull passed over (the game's relic is what a pull lands on first).
    /// </summary>
    private static void TheGamesRelic(Check c, Arena.Run run, int floor, string games, string? shown, string what)
    {
        if (RunRelics.IsBuilt(games)) c.Equal(games, shown, what);
        else c.That(LoggedOn(run, floor, "relic").Contains(games), $"{what}: the game gave {games}, which is not built, so the log of floor {floor} names it as passed over (it holds [{string.Join(", ", LoggedOn(run, floor, "relic"))}])");
    }

    private static void Recorded(Check c, RoomTraces.RecordedRun record)
    {
        var first = record.Rooms[0].Floor;
        c.Group($"the game's own record of the run on seed {record.Seed} ({record.Record}), floors {first} to {record.Rooms[^1].Floor}, through the rooms", () =>
        {
            var director = new Rooms
            {
                Script = record.Rooms.Select(room => room.Room).ToArray(), FirstFloor = first,
                RewardsAt = record.RewardsAt, Offset = record.Offset, Chance = record.Chance, Gone = record.GoneFromTheBags,
            };
            var run = new Arena.Run(new RunSetup { Seed = record.Seed }, director);
            foreach (var room in record.Rooms)
            {
                var floor = room.Floor;
                c.Equal(floor, run.State.TotalFloor, "the floor");
                switch (room.Room[0])
                {
                    case 'M' or 'E':
                    {
                        var rewards = ((RewardsScreen)run.Top!).Set.Rewards;
                        c.Equal(room.Gold, rewards.OfType<GoldReward>().First().Amount, $"floor {floor}: the gold");
                        c.Equal(room.Potion, rewards.OfType<PotionReward>().SingleOrDefault()?.Potion?.ClassName, $"floor {floor}: the potion");
                        var offered = rewards.OfType<CardReward>().Single().Cards.Select(card => card.Def.ClassName).ToList();
                        c.Sequence(room.Cards!.Order(StringComparer.Ordinal), offered.Order(StringComparer.Ordinal), $"floor {floor}: the three cards");
                        // The record lists a picked card first and the others in the order they were offered.
                        var left = room.FirstWasPicked ? room.Cards!.Skip(1) : room.Cards!;
                        c.Sequence(left, offered.Where(card => !room.FirstWasPicked || card != room.Cards![0]), $"floor {floor}: the cards that were left, in the order offered");
                        if (room.Relics is { } relics)
                            TheGamesRelic(c, run, floor, relics[0], rewards.OfType<RelicReward>().Single().Relic?.ClassName, $"floor {floor}: the elite's relic");

                        var before = run.Gold;
                        Do(run, "reward:0");
                        c.Equal(before + room.Gold, run.Gold, $"floor {floor}: the gold is taken");
                        Do(run, string.Create(CultureInfo.InvariantCulture, $"reward:{run.Snapshot().State["rewards"]!.AsArray().Count - 1}"));
                        Do(run, room.FirstWasPicked ? string.Create(CultureInfo.InvariantCulture, $"card:{offered.IndexOf(room.Cards![0])}") : "skip");
                        if (room.FirstWasPicked) c.Equal(room.Cards![0], run.State.Deck[^1].Def.ClassName, $"floor {floor}: the card the record's player took is in the deck");
                        Do(run, "proceed");
                        break;
                    }
                    case 'S':
                    {
                        var shelf = ((ShopScreen)run.Top!).Inventory;
                        var onSale = shelf.CharacterCards.Single(entry => entry.IsOnSale);
                        c.Equal(room.Bought, onSale.Card!.Def.ClassName, $"floor {floor}: the card on sale is the one the record's player bought");
                        c.Equal(room.BoughtFor, onSale.BaseCost, $"floor {floor}: for what the record says was spent on it");
                        c.Sequence(room.Cards!, shelf.CharacterCards.Where(entry => !entry.IsOnSale).Select(entry => entry.Card!.Def.ClassName), $"floor {floor}: the other four character cards, in shelf order");
                        // A colorless card that is not built is drawn again on the shops stream; from there on the shelf is not the game's.
                        if (Content.Registry.Has(ContentKind.Card, room.Colorless![0]))
                            c.Equal(room.Colorless![0], shelf.ColorlessCards[0].Card!.Def.ClassName, $"floor {floor}: the Uncommon colorless card");
                        else
                            c.Equal(room.Colorless![0], LoggedOn(run, floor, "card").FirstOrDefault(), $"floor {floor}: the Uncommon colorless card of the game is not built: the log names it first");
                        for (var place = 0; place < room.Relics!.Length; place++)
                            TheGamesRelic(c, run, floor, room.Relics[place], shelf.Relics[place].Relic?.ClassName, $"floor {floor}: relic place {place}");
                        Do(run, "proceed");
                        break;
                    }
                    case 'R':
                        Do(run, "option:0", "proceed");
                        break;
                    default:
                    {
                        var chest = (TreasureScreen)run.Top!;
                        TheGamesRelic(c, run, floor, room.Relics![0], chest.Relic?.ClassName, $"floor {floor}: the chest's relic");
                        Do(run, "open");
                        c.Equal(room.Gold, ((TreasureScreen)run.Top!).Gold, $"floor {floor}: the chest's gold");
                        Do(run, "relic:0", "proceed");
                        break;
                    }
                }
            }
            c.Equal(RunResult.Cleared, run.Result, "the stretch is walked to its end");
        });
    }

    // ---------- a roll exactly on its threshold ----------

    private static void Edges(Check c)
    {
        foreach (var edge in RoomTraces.Edges)
        {
            c.Group($"seed {edge.Seed}, the first hallway fight: {edge.What}", () =>
            {
                // What the fight gives with the two odds as named: a potion or none, and the first card with its rarity.
                (string? Potion, string Rarity, string Card) Roll(float offset, float chance)
                {
                    var run = new Arena.Run(new RunSetup { Seed = edge.Seed }, new Rooms { Script = ["M"], Offset = offset, Chance = chance });
                    var rewards = ((RewardsScreen)run.Top!).Set.Rewards;
                    var first = rewards.OfType<CardReward>().Single().Cards[0];
                    return (rewards.OfType<PotionReward>().SingleOrDefault()?.Potion?.ClassName, first.Def.Rarity.ToString(), first.Def.ClassName);
                }

                var on = Roll(edge.Offset, edge.Chance);
                c.Equal(edge.Potion, on.Potion, "on the threshold: the potion");
                c.Equal(edge.FirstRarity, on.Rarity, "on the threshold: the first card's rarity");
                c.Equal(edge.FirstCard, on.Card, "on the threshold: the first card");

                // The same fight with the threshold a few 32-bit float steps higher: the roll is below it now.
                var flipped = false;
                var (offset, chance) = (edge.Offset, edge.Chance);
                for (var step = 0; step < 4 && !flipped; step++)
                {
                    if (edge.Above == "potion") chance = MathF.BitIncrement(chance);
                    else offset = MathF.BitIncrement(offset);
                    var above = Roll(offset, chance);
                    flipped = edge.Above == "potion" ? above.Potion is not null : above.Rarity == edge.Above;
                }
                c.That(flipped, $"with the threshold a hair higher the roll is below it: {(edge.Above == "potion" ? "a potion drops" : $"the first card is {edge.Above}")}");
            });
        }
    }

    private static void ExactRolls(Check c)
    {
        foreach (var exact in RoomTraces.ExactRolls)
        {
            var isChest = exact.Room == "T";
            c.Group(string.Create(CultureInfo.InvariantCulture, $"seed {exact.Seed}: a {(isChest ? "chest's" : "potion's")} rarity roll of exactly {exact.Roll} is {exact.Rarity}"), () =>
            {
                // A potion chance of 2 makes the fight drop a potion whatever its roll says.
                var run = new Arena.Run(new RunSetup { Seed = exact.Seed },
                    new Rooms { Script = [exact.Room], RewardsAt = exact.RewardsAt, TreasureAt = exact.TreasureAt, Chance = isChest ? null : 2f });
                if (isChest)
                {
                    c.Equal(exact.Rarity, ((TreasureScreen)run.Top!).RolledRarity.ToString(), "the chest's rarity: a relic is Common below 0.5 and Uncommon below 0.83, not at them");
                    c.Equal(exact.TreasureAt + 1, Counters(run).Treasure, "one number of treasure_room_relics");
                }
                else
                {
                    var potion = ((RewardsScreen)run.Top!).Set.Rewards.OfType<PotionReward>().Single().Potion!;
                    c.Equal(exact.Rarity, potion.Rarity.ToString(), "the potion's rarity: Rare at or below 0.1, Uncommon at or below 0.35");
                    c.Equal(exact.Potion, potion.ClassName, "the potion");
                }
            });
        }
    }

    // ---------- the upgrade roll ----------

    /// <summary>A relic of the checks that sets the chance of a reward card coming upgraded.</summary>
    private sealed class UpgradeOdds : Relic, IRewardHooks
    {
        public decimal Odds;
        public override string Name => "Check Upgrade Odds";
        public override string Describe() => "Sets the chance that a reward card comes upgraded.";
        public decimal ModifyCardRewardUpgradeOdds(RunContext c, CardInstance card, decimal odds) => Odds;
    }

    private static void TheUpgradeRoll(Check c)
    {
        // The first fight of the first traced run: its three upgrade rolls are the numbers 5, 8 and 11 of rewards.
        // (trace_rooms.py, seed QEVNVN1RKF, room 1.) The game compares "roll at or below the chance" in decimals,
        // and a float becomes a decimal of seven digits.
        const string seed = "QEVNVN1RKF";
        float[] rolls = [0.0007037847535684705f, 0.9066170454025269f, 0.0578889362514019f];
        string[] cards = ["Bloodletting", "IronWave", "Headbutt"];

        c.Group("a reward card comes upgraded when its roll is AT or below the chance, not only below it", () =>
        {
            c.Equal(cards[0], RoomTraces.Runs[0].Rooms.OfType<RoomTraces.FightRoom>().First().Cards[0], "the fight is the first room of the first traced run");
            for (var card = 0; card < 3; card++)
            {
                var odds = (decimal)rolls[card];
                var run = new Arena.Run(new RunSetup { Seed = seed, ExtraRelics = [() => new UpgradeOdds { Odds = odds }] }, new Rooms { Script = ["M"] });
                var offered = ((RewardsScreen)run.Top!).Set.Rewards.OfType<CardReward>().Single().Cards;
                var expected = cards.Select((name, n) => name + (rolls[n] <= rolls[card] ? "+" : "")).ToList();
                c.Sequence(expected, offered.Select(o => o.Def.ClassName + (o.IsUpgraded ? "+" : "")),
                    string.Create(CultureInfo.InvariantCulture, $"with the chance set to card {card}'s own roll, {rolls[card]:R}"));
                c.That(expected[card].EndsWith('+'), "the card whose roll equals the chance is upgraded");
            }
        });

        c.Group("the upgrade roll is drawn whatever the card is: a card that cannot be upgraded takes its number too", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = seed }, new Rooms { Script = ["R"] });
            var curses = new CardCreationOptions([CardPools.Curse], CardCreationSource.Other, CardOddsType.Uniform);
            var made = CardFactory.CreateForReward(run, 1, curses)[0].Card;
            c.That(!made.IsUpgradable, $"a curse cannot be upgraded ({made.Def.ClassName})");
            c.Equal(2, World(run).Streams.Counter(RunStream.Rewards), "two numbers of rewards all the same: the pick and the upgrade roll (no rarity is rolled at uniform odds)");
        });
    }

    // ---------- places filled again ----------

    /// <summary>A relic of the checks that has every bought place of a shop filled again. (The game's The Courier does that
    /// and lowers prices besides; it is not built.)</summary>
    private sealed class Restocker : Relic
    {
        public override string Name => "Check Restocker";
        public override string Describe() => "Whatever is bought in a shop is stocked again.";
        public override bool ShouldRefillMerchantEntry(RunContext c, ShopEntry entry) => true;
    }

    private static void Refills(Check c)
    {
        const string seed = "YG4K5JKTX0";
        const int purse = 500;
        c.Group($"seed {seed}, the first shop with every bought place filled again: six purchases, traced", () =>
        {
            var shop = (RoomTraces.ShopRoomTrace)RoomTraces.Runs.Single(trace => trace.Seed == seed).Rooms[0];
            var run = new Arena.Run(new RunSetup { Seed = seed, Gold = purse, ExtraRelics = [() => new Restocker()] }, new Rooms { Script = ["S"] });
            ShopInventory Shelf() => ((ShopScreen)run.Top!).Inventory;
            c.Sequence(shop.Cards, Shelf().CardEntries.Select(entry => entry.Card!.Def.ClassName), "the shelf is the traced shop's: its cards");
            c.Sequence(shop.Potions, Shelf().Potions.Select(entry => entry.Potion!.ClassName), "and its potions");
            c.Equal(shop.After, Counters(run), "the counters before the first purchase");

            Do(run, "open");
            var gold = purse;
            foreach (var refill in RoomTraces.Refilled)
            {
                var isCard = refill.Buy.StartsWith("card", StringComparison.Ordinal);
                var place = int.Parse(refill.Buy[^1..], CultureInfo.InvariantCulture);
                var bought = isCard ? Shelf().CharacterCards[place].Card!.Def.ClassName : Shelf().Potions[place].Potion!.ClassName;
                Do(run, "buy:" + refill.Buy);
                gold -= refill.Paid;
                c.Equal(gold, run.Gold, $"{refill.Buy} ({bought}): {refill.Paid} gold is paid");
                if (isCard)
                {
                    var entry = Shelf().CharacterCards[place];
                    c.Equal(refill.Now, entry.Card?.Def.ClassName, $"{refill.Buy}: the card that takes the place");
                    c.Equal(refill.Price, entry.BaseCost, $"{refill.Buy}: its price");
                    c.That(!entry.IsOnSale, $"{refill.Buy}: a place filled again is not on sale");
                    c.Equal(bought, run.State.Deck[^1].Def.ClassName, $"{refill.Buy}: the bought card is in the deck");
                }
                else
                {
                    var entry = Shelf().Potions[place];
                    c.Equal(refill.Now, entry.Potion?.ClassName, $"{refill.Buy}: the potion that takes the place");
                    c.Equal(refill.Price, entry.BaseCost, $"{refill.Buy}: its price");
                    c.That(run.Potions.Any(potion => potion?.ClassName == bought), $"{refill.Buy}: the bought potion is in the belt");
                }
                c.Equal(refill.After, Counters(run), $"{refill.Buy}: rewards, shops and treasure_room_relics stand at");
            }
            c.Sequence(shop.Relics.Length == 3 ? shop.RelicPrices : [], Shelf().Relics.Select(entry => entry.BaseCost), "the relic places were not touched");
        });
    }

    // ---------- the potion chance in float steps ----------

    private static void ThePotionChance(Check c)
    {
        c.Group("the potion chance moves in 32-bit float steps: after a fourth drop in a row it is 1.4901161e-08, not 0", () =>
        {
            // Three drops from the start leave 0.4 - 0.1 - 0.1 - 0.1 = 0.10000001639127731 in floats. The first number
            // of rewards on seed 1BUSLN0FY5 is 0.0585: a fourth drop. (trace_rooms.py: trace 1BUSLN0FY5 M,M chance=0.10000001639127731)
            const float afterThree = 0.10000001639127731f;
            c.Equal(afterThree, 0.4f - 0.1f - 0.1f - 0.1f, "the chance after three drops, in this machine's float arithmetic");
            var run = new Arena.Run(new RunSetup { Seed = "1BUSLN0FY5" }, new Rooms { Script = ["M"], Chance = afterThree });
            c.That(((RewardsScreen)run.Top!).Set.Rewards.OfType<PotionReward>().Any(), "a potion drops");
            c.Equal(1.4901161193847656e-08f, run.State.PotionChance, "the chance afterwards (64-bit arithmetic would leave 1.639e-08)");
        });
    }

    // ---------- the number a rest names ----------

    private static void TheRestsNumber(Check c)
    {
        c.Group("a rest of 24.6 HP is named as 24 and heals 50 HP to 74: the amount is cut, not rounded", () =>
        {
            // 30% of 82 max HP is 24.6. The mod's label is "heal" and the amount cut to a whole number (RestSiteReader.cs:537);
            // the game adds the amount to the HP and cuts the sum (Creature.SetCurrentHpInternal): 50 + 24.6 is 74.
            var run = new Arena.Run(new RunSetup { Seed = "QEVNVN1RKF", Hp = 50, MaxHp = 82 }, new Rooms { Script = ["R"] });
            c.Equal(24.6m, RestSite.HealAmount(run), "30% of 82");
            c.Equal("Rest (heal 24)", run.Snapshot().Actions.Single(action => action.Id == "option:0").Label, "the label");
            Do(run, "option:0");
            c.Equal(74, run.Hp, "50 + 24.6, cut");
        });
    }

    // ---------- prices on a half ----------

    private static void PriceTies(Check c)
    {
        c.Group("a price is the table value times the factor as a 32-bit float, and a half goes to the even number", () =>
        {
            c.That(RoomTraces.PriceTies.Any(tie => tie.Kind == "card") && RoomTraces.PriceTies.Any(tie => tie.Kind == "relic") && RoomTraces.PriceTies.Any(tie => tie.Kind == "potion"),
                "a card, a relic and a potion are among the traced prices: each kind has its own line of arithmetic");
            foreach (var kind in new[] { "card", "relic", "potion" })
            {
                var ofKind = RoomTraces.PriceTies.Where(tie => tie.Kind == kind).ToList();
                c.That(ofKind.Any(tie => tie.Price != tie.HalfUp), $"a {kind} price that half-up rounding would get wrong is among them");
                c.That(ofKind.Any(tie => tie.Price != tie.InDoubles), $"a {kind} price that 64-bit arithmetic would get wrong is among them");
            }
        });

        foreach (var tie in RoomTraces.PriceTies)
        {
            c.Group(string.Create(CultureInfo.InvariantCulture,
                $"the price of the first {tie.Kind} of a shop rolled on seed {tie.Seed} with rewards at {tie.RewardsAt} and shops at {tie.ShopsAt}: {tie.TableValue} x {tie.Factor:R}"), () =>
            {
                var run = new Arena.Run(new RunSetup { Seed = tie.Seed }, new Rooms { Script = ["S"], RewardsAt = tie.RewardsAt, ShopsAt = tie.ShopsAt });
                var shelf = ((ShopScreen)run.Top!).Inventory;
                ShopEntry entry;
                switch (tie.Kind)
                {
                    case "card":
                        var card = shelf.CharacterCards[0];
                        c.Equal(tie.Thing, card.Card!.Def.ClassName, "the card");
                        c.That(!card.IsOnSale, "it is not the card on sale");
                        c.Equal(tie.TableValue, ShopPrices.CardBase(card.Card), "its table value");
                        entry = card;
                        break;
                    case "relic":
                        var relic = shelf.Relics[0];
                        c.Equal(tie.Thing, GameLists.BagRelic(relic.Relic!.ClassName).Rarity.ToString(), "the relic's list");
                        c.Equal(tie.TableValue, ShopPrices.RelicBase(GameLists.BagRelic(relic.Relic.ClassName).Rarity), "its table value");
                        entry = relic;
                        break;
                    default:
                        var potion = shelf.Potions[0];
                        c.Equal(tie.Thing, potion.Potion!.ClassName, "the potion");
                        c.Equal(tie.TableValue, ShopPrices.PotionBase(potion.Potion.Rarity), "its table value");
                        entry = potion;
                        break;
                }
                // The trace's own claim, in the arithmetic of this machine: the product is a whole number and a half.
                var product = (float)tie.TableValue * tie.Factor;
                c.Equal(0.5f, product - MathF.Floor(product), "the product lies on a half");
                c.That(tie.Price != tie.HalfUp || tie.Price != tie.InDoubles, "another arithmetic would give another price");
                c.Equal(tie.Price, entry.BaseCost, string.Create(CultureInfo.InvariantCulture,
                    $"the price (half-up would give {tie.HalfUp}, 64-bit arithmetic {tie.InDoubles})"));
            });
        }
    }
}
