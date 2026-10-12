using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The first floors of a real run, with the pieces of run mode working together: the world of
/// the seed, the map and its screen, fights of the act's own list on the run's streams, their
/// rewards through the rewards and card reward screens, cards going into the deck, an Unknown
/// room, a relic pull and the run's hooks. Each piece has its own checks; this one holds that
/// they fit, on one run, through the screens a player sees.
/// <para>
/// Every expected value is the real game's: Mark's run on seed 1BUSLN0FY5 (v0.107.1), as its
/// saves and the game's own record of it have it (sts2-notes/golden-saves: the map of the save
/// at Neow, history/1791755111.run, the later saves). Neow took Arcane Scroll, which gave
/// Barricade. Floor 2, at (5,1): Shrinker Beetle; 11 gold, no potion, the cards Colossus
/// (taken), Unrelenting, Havoc. Floor 3, at (6,2): Fuzzy Wurm Crawler; 10 gold, a Vulnerable
/// Potion, the cards Fight Me! (taken), Stone Armor, Setup Strike. Floor 4, at (6,3): an
/// Unknown room that was an event, This or That, whose relic was Stone Cracker. Floor 5, at
/// (6,4): a shop. After the fights the <c>rewards</c> stream stood at 12 and 25, and at 26
/// after the event's relic.
/// </para>
/// <para>
/// How the fights are played does not show in any of that: a fight draws from the run's other
/// streams, never from <c>rewards</c>. They are played by the Greedy player. Neow and the event
/// are not built yet, so the director of this check does by hand the one thing each did in the
/// real run that drew a number: Arcane Scroll's card, and the event's relic pull.
/// </para>
/// </summary>
public sealed class RealRunChecks : IArenaChecks
{
    private const string PlayedSeed = "1BUSLN0FY5";

    public void Run(Check c)
    {
        FirstFloors(c);
        StrictRun(c);
    }

    // ---------- the director of the check ----------

    /// <summary>
    /// The act as far as it is built: the map, hallway fights from the act's list with their
    /// rewards, and a stand-in for every room that is not built yet.
    /// </summary>
    private sealed class Floors : RunDirector, IMapDirector
    {
        /// <summary>The game's streams with nothing drawn again.</summary>
        public bool Strict { get; init; }

        public List<string> Encounters = new();
        public string? Event;

        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            var map = MapState.BeginAct(run, 0);
            var arrival = map.Enter(run, map.Map.Start.Coord);
            run.AfterRoomEntered(arrival.Room);

            // Neow is not built. What the real run took there, Arcane Scroll, added one Rare of the
            // character's pool: one pick at uniform odds, no upgrade roll (ArcaneScroll.cs).
            var options = new CardCreationOptions([CardPools.Character], CardCreationSource.Other, CardOddsType.Uniform, static card => card.Rarity == CardRarity.Rare)
                .WithFlags(CardCreationFlags.NoUpgradeRoll);
            DeckEdits.Add(run, CardFactory.CreateForReward(run, 1, options)[0].Card);
            MapState.MarkRoomVisited(run, arrival.Room);
            run.Push(new MapScreen());
        }

        public void EnterRoom(Arena.Run run, MapArrival arrival)
        {
            var rooms = run.State.Part<RunWorld>().Rooms[run.State.ActIndex];
            if (arrival.Room == RoomKind.Monster)
            {
                Encounters.Add(rooms.NextNormalEncounter);
                run.StartFight(new FightRequest { Encounter = rooms.NextNormalEncounter });
                MapState.MarkRoomVisited(run, arrival.Room);
                return;
            }

            if (arrival.Room == RoomKind.Event) Event = rooms.NextEvent;
            MapState.MarkRoomVisited(run, arrival.Room);
            run.AfterRoomEntered(arrival.Room);
            run.Push(new StandIn(arrival.Room));
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight)
        {
            if (RunRewards.OfferForRoomEnd(run, fight) is null) AfterRoom(run);
        }

        public override void AfterRoom(Arena.Run run)
        {
            run.CloseAll();
            run.Push(new MapScreen());
        }

        public override RunDirector Clone(CloneMap map) => new Floors { Strict = Strict, Encounters = Encounters.ToList(), Event = Event };
    }

    /// <summary>
    /// A room that is not built yet: it can be left, and an event room can first pull the relic
    /// This or That gave (a rarity rolled on <c>rewards</c>, then the front of the player's bag).
    /// </summary>
    private sealed class StandIn(RoomKind kind) : RunScreen
    {
        public string Pulled = "";

        public override string Name => "event";

        public override GameSnapshot Snapshot(Arena.Run run)
        {
            var actions = new List<LegalAction>();
            if (kind == RoomKind.Event && Pulled.Length == 0) actions.Add(new LegalAction("pull", "Pull a relic"));
            actions.Add(new LegalAction("proceed", "Proceed"));
            return new GameSnapshot
            {
                Screen = Name, AwaitingDecision = true,
                State = new JsonObject { ["run"] = run.RunJson(), ["room"] = kind.ToString(), ["pulled"] = Pulled },
                Actions = actions,
            };
        }

        public override ActionResult Execute(Arena.Run run, string actionId)
        {
            if (actionId == "proceed")
            {
                run.RoomFinished();
                return ActionResult.Success;
            }
            if (actionId != "pull" || kind != RoomKind.Event || Pulled.Length > 0) return ActionResult.Fail($"Unknown action '{actionId}'.");

            var state = run.State;
            var strict = state.FindPart<LeftOutLog>()?.Strict ?? false;
            var rng = state.Part<RunWorld>().Streams.Draw(RunStream.Rewards, Purpose.RelicRarity(0));
            var pull = RunRelics.RollAndPullFromFront(state, rng, strict);
            Pulled = $"{pull.Asked}: the game gives {pull.GameRelic}";
            // A run that must be the game's own cannot go on without the game's relic.
            if (!pull.Found && strict) throw new RunUnrepresentableException(pull.Reason!);
            if (pull.Found) run.ObtainRelic(pull.Relic!);
            return ActionResult.Success;
        }

        public override RunScreen Clone(CloneMap map) => new StandIn(kind) { Pulled = Pulled };
    }

    // ---------- helpers ----------

    private static readonly IPolicy Greedy = Policies.All.Single(policy => policy.Name == "Greedy");

    /// <summary>Plays the fight that is on to its end.</summary>
    private static void PlayFight(Arena.Run run)
    {
        for (var n = 0; n < 2000 && run.Fight is not null; n++) Do(run, Greedy.Choose(run.Snapshot()));
    }

    private static RunWorld World(Arena.Run run) => run.State.Part<RunWorld>();

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

            // Floor 2: the first hallway fight of the act's list, on the run's streams.
            Do(run, "map:2");
            c.Equal("combat", run.Snapshot().Screen, "travelling to a Monster point starts its fight");
            c.Equal(2, run.State.TotalFloor, "on floor 2");
            c.Equal("ShrinkerBeetleWeak", run.Fight!.EncounterName, "the first entry of the act's hallway list");
            c.That(((FightScreen)run.Top!).OnRunStreams, "on the run's own streams");
            PlayFight(run);
            c.That(run.Fight is null && !run.IsOver, "the fight is won");

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
            c.Equal("Event", s.State["room"]!.GetValue<string>(), "the Unknown room is an event");
            c.Equal(4, run.State.TotalFloor, "on floor 4");
            var odds = World(run).UnknownOdds;
            c.Equal((0.2f, -2f, 0.04f, 0.06f), (odds.Monster, odds.Elite, odds.Treasure, odds.Shop), "the odds of the next Unknown room, as the later save has them");
            c.Equal("ThisOrThat", ((Floors)run.Director).Event, "the event of the list: entry 1, since Neow's room took the counter to 1");
            c.Equal(2, World(run).Rooms[0].EventsVisited, "and the counter is at 2 now");
            c.Equal(RoomKind.Event, run.Room, "the hooks were told an Event room");

            Do(run, "pull");
            c.Equal("Uncommon: the game gives StoneCracker", run.Snapshot().State["pulled"]!.GetValue<string>(), "the 26th number of rewards rolls Uncommon, and the front of the player's Uncommon list is Stone Cracker");
            c.Equal(26, RewardsCounter(run), "one number for the rarity");
            Do(run, "proceed");

            // Floor 5: the shop.
            s = run.Snapshot();
            c.Equal("(5,4) monster (6,4) shop", NextPlaces(s), "two ways on from the event's point, as the saved map has them");
            Do(run, "map:1");
            c.Equal("Shop", run.Snapshot().State["room"]!.GetValue<string>(), "the shop of floor 5");
            c.Equal(5, run.State.TotalFloor, "on floor 5");
            c.Equal(RoomKind.Shop, run.Room, "the hooks were told a Shop room");
            c.Sequence(["ShrinkerBeetleWeak", "FuzzyWurmCrawlerWeak"], ((Floors)run.Director).Encounters, "the fights met, in the order of the game's record");
            c.Equal("(3,0) (5,1) (6,2) (6,3) (6,4)", string.Join(" ", run.State.Part<MapState>().Visited), "the path of the save");
            c.Sequence([RoomKind.Event, RoomKind.Monster, RoomKind.Monster, RoomKind.Event, RoomKind.Shop], run.State.Part<MapState>().History.Select(visit => visit.Rooms[0]), "and the room of each point");

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
            Do(run, "map:2");
            PlayFight(run);
            var offer = Offered(TakeGoldAndOpenCards(run));
            Do(run, $"card:{offer.IndexOf("Colossus")}", "proceed", "map:0");
            PlayFight(run);
            Do(run, "reward:1");
            offer = Offered(TakeGoldAndOpenCards(run));
            Do(run, $"card:{offer.IndexOf("Fight Me!")}", "proceed", "map:0");
            c.That(!run.IsOver && run.State.TotalFloor == 4 && RewardsCounter(run) == 25, "everything up to the event's room is built: the run stands on floor 4 with rewards at 25");

            var before = run.Clone();
            Do(run, "pull");
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
            c.Equal("pull", run.Actions[^1], "the action stays in the save");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new Floors { Strict = true }, run.Actions).Dump(), "which replays to the same end");
        });
    }
}
