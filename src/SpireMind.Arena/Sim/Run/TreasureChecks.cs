using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The treasure room: when its relic is settled and from which bag, what the chest pays and
/// from which stream, the rewards a relic adds, the screen as the mod shows it, and what
/// happens when the game's relic is one the arena has not built.
/// <para>
/// The anchor is the real game's: the chest of Mark's winning run on seed 75F277YN2G, as the
/// game's own record of it has it (sts2-notes/golden-saves/history/1791592972.run, floor 10):
/// the relic Strike Dummy and 51 gold, which took the player from 116 to 167. Both follow from
/// the seed and from what that run had drawn by then:
/// </para>
/// <list type="bullet">
/// <item>The relic: the first number of the run's <c>treasure_room_relics</c> stream rolls
/// Common, and Strike Dummy is the front of the Common list of the SHARED bag. (The shop of
/// floor 3 had shown Lantern, Red Skull and Burning Sticks and the elite of floor 7 had given
/// Happy Flower; none of them stood before it.)</item>
/// <item>The gold: the 85th number of the player's <c>rewards</c> stream. The 84 before it,
/// by the record: floor 2, a hallway fight without a potion, 11 (the potion roll, the gold,
/// three numbers for each of three cards); the shop of floor 3, 14; floors 4, 5 and 6, 11
/// each; the elite of floor 7 with a potion and its relic, 14; floor 9, 12: the 11 of a
/// hallway fight and one for the gold the Gremlin Merc had stolen, which comes back as a gold
/// row of a fixed amount that the reward set was made with, and such a row still draws its
/// number (HeistPower.cs, GoldReward.cs:69-73).</item>
/// </list>
/// <para>
/// That run's first act is Underdocks, which the arena cannot play; the chest does not ask
/// which act it stands in. The relics of the checks are stand-ins under the game's class
/// names (a chest's relic leaves the player's bag by its class name); they are the checks'
/// own and are never registered.
/// </para>
/// </summary>
public sealed class TreasureChecks : IArenaChecks
{
    public void Run(Check c)
    {
        RealChest(c);
        OrderOfEvents(c);
        TwoQuestions(c);
        GoldRange(c);
        GoldHooks(c);
        Screen(c);
        EmptyChest(c);
        ExtraRewards(c);
        RelicsNotBuilt(c);
        FirstRunChest(c);
        KeyedStreams(c);
        Refusals(c);
        OnTheMap(c);
        RandomWalks(c);
        ReplayAndCopy(c);
    }

    // ---------- the directors and screens of the checks ----------

    /// <summary>A run that stands in front of a treasure room: <c>enter</c> goes in, and a finished room leads back here.</summary>
    private sealed class Vault : RunDirector
    {
        public StreamMode Mode { get; init; } = StreamMode.Game;
        public RunUnlocks? Unlocks { get; init; }
        public bool Strict { get; init; }

        /// <summary>The total floor of the first chest.</summary>
        public int Floor { get; init; } = 10;

        /// <summary>The map place the streams are told (keyed streams read it).</summary>
        public int Column { get; init; } = 3;

        public Func<string, bool> IsBuilt { get; init; } = static _ => true;
        public Func<string, Relic> NewRelic { get; init; } = static name => StandIn(name);

        /// <summary>What the run had done before the chest, as far as the chest can tell: streams moved on, relics gone from the bags.</summary>
        public Action<Arena.Run>? Before { get; init; }

        public int RoomsLeft;

        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State, Unlocks, Mode);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            run.State.TotalFloor = Floor - 1;
            Before?.Invoke(run);
            run.Push(new Door());
        }

        public void Enter(Arena.Run run)
        {
            var state = run.State;
            state.TotalFloor++;
            state.Part<RunWorld>().Streams.MoveTo(state.ActIndex, 9, Column);
            TreasureRoom.Enter(run, IsBuilt, NewRelic);
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => throw new NotSupportedException("No fight is part of these checks.");

        public override void AfterRoom(Arena.Run run)
        {
            RoomsLeft++;
            run.CloseAll();
            run.Push(new Door());
        }
    }

    /// <summary>The way into the room: one action, so that entering is an action of the run like the map's.</summary>
    private sealed class Door : RunScreen
    {
        public override string Name => "map";

        public override GameSnapshot Snapshot(Arena.Run run) => new()
        {
            Screen = Name, AwaitingDecision = true, State = new JsonObject { ["run"] = run.RunJson() }, Actions = [new LegalAction("enter", "Enter the treasure room")],
        };

        public override ActionResult Execute(Arena.Run run, string actionId)
        {
            if (actionId != "enter") return ActionResult.Fail($"Unknown map action '{actionId}'.");
            run.Pop();
            ((Vault)run.Director).Enter(run);
            return ActionResult.Success;
        }

        public override RunScreen Clone(CloneMap map) => new Door();
    }

    // ---------- the relics of the checks ----------

    /// <summary>The checks' stand-in for the real run's chest relic. It notes its pickup: how many relics the player held and whether the bags still held it.</summary>
    private sealed class StrikeDummy : Relic
    {
        public string Heard = "";
        public override string Name => "Strike Dummy";
        public override string Describe() => "(the checks' stand-in for Strike Dummy)";

        public override void AfterObtained(RunContext c)
        {
            var world = c.NeedRun("a look at the bags").State.Part<RunWorld>();
            Heard += $"obtained as relic {c.Relics.Count}, in the bags: {world.PlayerBag.Contains(ClassName) || world.SharedBag.Contains(ClassName)},";
        }
    }

    private sealed class FestivePopper : Relic
    {
        public override string Name => "Festive Popper";
        public override string Describe() => "(the checks' stand-in for Festive Popper)";
    }

    private sealed class Gorget : Relic
    {
        public override string Name => "Gorget";
        public override string Describe() => "(the checks' stand-in for Gorget)";
    }

    /// <summary>A stand-in for any other relic a chest of the checks may hold; it goes by the title it is given.</summary>
    private sealed class Loot(string title) : Relic
    {
        public override string Name => title;
        public override string Describe() => "(the checks' stand-in)";
    }

    private static Relic StandIn(string className) => className switch
    {
        "StrikeDummy" => new StrikeDummy(),
        "FestivePopper" => new FestivePopper(),
        "Gorget" => new Gorget(),
        _ => new Loot(className),
    };

    /// <summary>A relic that keeps the game's defaults and notes the hooks of a treasure room as it hears them.</summary>
    private sealed class Witness : Relic
    {
        public string Heard = "";
        public override string Name => "Witness";
        public override string Describe() => "(a check's listener)";
        public override void AfterRoomEntered(RunContext c, RoomKind room) => Heard += $"entered {room},";

        public override bool ShouldGenerateTreasure(RunContext c)
        {
            Heard += "treasure?,";
            return true;
        }

        public override decimal ModifyGoldGained(RunContext c, decimal amount)
        {
            Heard += $"gold {amount},";
            return amount;
        }

        public override void AfterGoldGained(RunContext c) => Heard += $"gained at {c.Gold},";
    }

    /// <summary>SilverCrucible.cs, the chest's part: it counts the treasure rooms entered and leaves the first one empty.</summary>
    private sealed class SilverCrucible : Relic
    {
        public int TreasureRoomsEntered;
        public override string Name => "Silver Crucible";
        public override string Describe() => "The first chest you open is empty.";

        public override void AfterRoomEntered(RunContext c, RoomKind room)
        {
            if (room == RoomKind.Treasure) TreasureRoomsEntered++;
        }

        public override bool ShouldGenerateTreasure(RunContext c) => TreasureRoomsEntered > 1;
    }

    /// <summary>A relic of the checks alone whose answers to "is there treasure" are scripted: one letter per question, y or n.</summary>
    private sealed class Fickle : Relic
    {
        public string Answers { get; init; } = "";
        public int Asked;
        public override string Name => "Fickle";
        public override string Describe() => "(a check's relic)";
        public override bool ShouldGenerateTreasure(RunContext c) => Answers[Asked++] == 'y';
    }

    /// <summary>A relic of the checks alone that doubles gold gained.</summary>
    private sealed class Midas : Relic
    {
        public override string Name => "Midas";
        public override string Describe() => "(a check's relic) Gain twice the gold.";
        public override decimal ModifyGoldGained(RunContext c, decimal amount) => amount * 2m;
    }

    /// <summary>A relic of the checks alone that adds 7 gold to a treasure room's rewards, and to no other room's.</summary>
    private sealed class Bonus : Relic, IRewardHooks
    {
        public string Heard = "";
        public override string Name => "Bonus";
        public override string Describe() => "(a check's relic) Chests also hold a reward of 7 gold.";

        public bool TryModifyRewards(RunContext c, List<Reward> rewards, RoomKind? room)
        {
            Heard += $"rewards of {room?.ToString() ?? "nobody"},";
            if (room != RoomKind.Treasure) return false;
            rewards.Add(new GoldReward(7));
            return true;
        }

        public override void AfterModifyingRewards(RunContext c) => Heard += "told,";
    }

    // ---------- helpers ----------

    /// <summary>Mark's winning run of 2026-10-09: the game's record of it is the evidence for the chest.</summary>
    private const string RealSeed = "75F277YN2G";

    /// <summary>How many numbers of <c>rewards</c> that run had drawn before its chest (see the class).</summary>
    private const int RewardsBeforeTheChest = 11 + 14 + 11 + 11 + 11 + 14 + 12;

    /// <summary>The real run as it stood before its chest on floor 10: 38 HP, 116 gold, <c>rewards</c> at 84, and four relics gone from the bags.</summary>
    private static RunSetup RealSetup(params Func<Relic>[] relics) => new() { Seed = RealSeed, Hp = 38, Gold = 116, ExtraRelics = relics };

    private static readonly Action<Arena.Run> RealHistory = static run =>
    {
        World(run).Streams.FastForward(RunStream.Rewards, RewardsBeforeTheChest);
        // The shop's three left both bags when they were put on the shelf; the elite's relic when it was obtained.
        foreach (var gone in new[] { "Lantern", "RedSkull", "BurningSticks", "HappyFlower" }) RunRelics.TakeOutOfBags(run.State, gone);
    };

    private static Arena.Run RealRun(Vault? director = null, params Func<Relic>[] relics) =>
        new(RealSetup(relics), director ?? new Vault { Before = RealHistory });

    private static RunWorld World(Arena.Run run) => run.State.Part<RunWorld>();

    private static int Counter(Arena.Run run, RunStream stream) => World(run).Streams.Counter(stream);

    private static TreasureScreen Chest(Arena.Run run) => (TreasureScreen)run.Top!;

    private static string RelicNames(Arena.Run run) => string.Join(", ", run.State.Relics.Select(relic => relic.Name));

    // ---------- 1. the real game's chest ----------

    private static void RealChest(Check c)
    {
        c.Group("the chest of a real run (seed 75F277YN2G, floor 10): Strike Dummy and 51 gold, from the seed and the run's draws", () =>
        {
            var director = new Vault { Before = RealHistory };
            var setup = RealSetup();
            var run = new Arena.Run(setup, director);
            c.Equal("Underdocks", World(run).Acts[0], "the seed's first act, as the record has it");
            c.Equal(84, RewardsBeforeTheChest, "numbers of rewards drawn before the chest, by the record");
            c.Equal((0, 84), (Counter(run, RunStream.TreasureRoomRelics), Counter(run, RunStream.Rewards)), "the streams before the room");

            // Entering: the relic is settled here, long before the chest is opened.
            Do(run, "enter");
            c.Equal("treasure", run.Snapshot().Screen, "the treasure room's screen");
            c.Equal(10, run.State.TotalFloor, "on floor 10");
            c.Equal(RoomKind.Treasure, run.Room, "the room the hooks were told about");
            c.Equal((1, 84), (Counter(run, RunStream.TreasureRoomRelics), Counter(run, RunStream.Rewards)),
                "one number of treasure_room_relics for the rarity; nothing of rewards yet");
            c.Equal(RelicRarity.Common, Chest(run).RolledRarity, "the first number of the stream rolls Common");
            c.Equal("StrikeDummy", Chest(run).GameRelic, "the front of the shared bag's Common list: the relic of the real chest");
            c.Equal("StrikeDummy", Chest(run).Relic?.ClassName, "and it is the relic in the chest");
            c.That(!World(run).SharedBag.Contains("StrikeDummy"), "it has left the shared bag");
            c.That(World(run).PlayerBag.Contains("StrikeDummy"), "but not the player's: a chest pulls from the shared bag alone");
            c.Equal(116, run.Gold, "no gold before the chest is opened");

            // Opening: the gold, at once.
            Do(run, "open");
            c.Equal(167, run.Gold, "116 + 51: the gold of the real chest");
            c.Equal(51, Chest(run).Gold, "the chest paid 51");
            c.Equal((1, 85), (Counter(run, RunStream.TreasureRoomRelics), Counter(run, RunStream.Rewards)), "one number of rewards for the gold");
            c.Equal("treasure", run.Snapshot().Screen, "no rewards screen: nothing added rewards to the room");
            c.Equal("relic:0=Take relic Strike Dummy", Labels(run.Snapshot()), "the relic must be taken: no proceed beside it");
            c.Equal("Burning Blood", RelicNames(run), "not obtained yet");

            // Taking the relic.
            Do(run, "relic:0");
            c.Equal("Burning Blood, Strike Dummy", RelicNames(run), "obtained, at the end of the relics");
            c.Equal("obtained as relic 2, in the bags: False,", run.State.FindRelic<StrikeDummy>()!.Heard,
                "its pickup effect ran once, after it had joined the relics and left both bags");
            c.That(!World(run).PlayerBag.Contains("StrikeDummy"), "it has left the player's bag now");
            c.Equal("proceed=Leave the treasure room", Labels(run.Snapshot()), "proceed is offered once the relic is taken");

            Do(run, "proceed");
            c.Equal("map", run.Snapshot().Screen, "proceed finishes the room");
            c.Equal(1, ((Vault)run.Director).RoomsLeft, "once");
            c.Equal((38, 167), (run.Hp, run.Gold), "38 HP and 167 gold, as the record has the run after floor 10");
            c.Sequence(["enter", "open", "relic:0", "proceed"], run.Actions, "the save of the room");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, director, run.Actions).Dump(), "which replays to the same state");
        });
    }

    // ---------- 2. what happens when ----------

    private static void OrderOfEvents(Check c)
    {
        c.Group("entering sends 'after room entered' and then asks about the treasure; opening asks again and pays through the gold hooks", () =>
        {
            var run = RealRun(null, () => new Witness());
            c.Equal("", run.State.FindRelic<Witness>()!.Heard, "nothing before the room");
            Do(run, "enter");
            c.Equal("entered Treasure,treasure?,", run.State.FindRelic<Witness>()!.Heard, "room entered first, then the question, once");
            Do(run, "open");
            c.Equal("entered Treasure,treasure?,treasure?,gold 51,gained at 167,", run.State.FindRelic<Witness>()!.Heard,
                "the question again, then the gold through 'modify gold gained' and 'gold gained'");
            Do(run, "relic:0", "proceed");
            c.Equal("entered Treasure,treasure?,treasure?,gold 51,gained at 167,", run.State.FindRelic<Witness>()!.Heard, "taking the relic and leaving ask nothing more");
        });
    }

    private static void TwoQuestions(Check c)
    {
        c.Group("the relic goes by the answer on entering, the gold by the answer on opening", () =>
        {
            // No on entering, yes on opening: no relic was rolled, but the chest pays.
            var run = RealRun(null, () => new Fickle { Answers = "ny" });
            Do(run, "enter");
            c.Equal(0, Counter(run, RunStream.TreasureRoomRelics), "no on entering: no rarity is rolled");
            c.That(Chest(run).Relic is null && Chest(run).GameRelic is null, "and the chest holds no relic");
            Do(run, "open");
            c.Equal(167, run.Gold, "yes on opening: the 51 gold all the same");
            c.Equal("proceed=Leave the treasure room (no relic on offer)", Labels(run.Snapshot()), "nothing to take");

            // Yes on entering, no on opening: the relic, and no gold.
            run = RealRun(null, () => new Fickle { Answers = "yn" });
            Do(run, "enter", "open");
            c.Equal(116, run.Gold, "no on opening: no gold");
            c.Equal(RewardsBeforeTheChest, Counter(run, RunStream.Rewards), "and no number of rewards");
            c.Equal(0, Chest(run).Gold, "the chest paid nothing");
            c.Equal("relic:0=Take relic Strike Dummy", Labels(run.Snapshot()), "but the relic rolled on entering is there");
            c.Equal(2, run.State.FindRelic<Fickle>()!.Asked, "asked twice in all");
        });
    }

    // ---------- 3. the gold ----------

    private static void GoldRange(Check c)
    {
        c.Group("a chest pays 42 to 52 gold: one number of rewards, drawn when it is opened", () =>
        {
            var seen = new SortedSet<int>();
            var wrong = new List<string>();
            for (var n = 0; n < 300; n++)
            {
                var run = new Arena.Run(new RunSetup { Seed = $"CHEST{n:000}", Gold = 0 }, new Vault());
                Do(run, "enter");
                // A second reading of the roll: the next number of the long stream, as the game's NextInt(42, 53) scales it.
                var expected = World(run).Streams.Long(RunStream.Rewards).Clone().NextInt(42, 53);
                var before = Counter(run, RunStream.Rewards);
                Do(run, "open");
                seen.Add(run.Gold);
                if (run.Gold != expected || Chest(run).Gold != expected || Counter(run, RunStream.Rewards) != before + 1)
                    wrong.Add($"CHEST{n:000}: {run.Gold} gold, expected {expected}");
            }
            c.Equal("", string.Join("; ", wrong), "every chest of 300 seeds pays the next number of rewards scaled to 42 to 52, and draws that one number");
            c.Equal((42, 52), (seen.Min, seen.Max), "the least and the most gold over 300 chests");
            c.Equal(11, seen.Count, "and every amount between them occurs");
        });
    }

    private static void GoldHooks(Check c)
    {
        c.Group("the chest's gold is gained through the gold hooks", () =>
        {
            var run = RealRun(null, () => new Midas());
            Do(run, "enter", "open");
            c.Equal(116 + 102, run.Gold, "51 doubled");
            c.Equal(102, Chest(run).Gold, "the chest notes what was paid");
        });
    }

    // ---------- 4. the screen ----------

    private static void Screen(Check c)
    {
        c.Group("the treasure screen as the mod shows it (docs/screens.md, Treasure)", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, Hp = 38, Gold = 116, Potions = ["BloodPotion"] };
            var run = new Arena.Run(setup, new Vault { Before = RealHistory });
            Do(run, "enter");

            var s = run.Snapshot();
            c.Equal("treasure", s.Screen, "the screen");
            c.That(s.AwaitingDecision, "a decision");
            c.Equal("run,chest_open,player,potions", Keys(s.State), "before the chest is open: no relics, no boss");
            c.Equal("""{"act":1,"floor":10,"ascension":0}""", Text(s.State["run"]), "the run block");
            c.Equal(false, s.State["chest_open"]!.GetValue<bool>(), "the chest is shut");
            c.Equal("""{"character":"The Ironclad","hp":38,"max_hp":80,"gold":116,"relics":["Burning Blood"],"deck_size":10,"deck":{"Bash":1,"Defend":4,"Strike":5}}""",
                Text(s.State["player"]), "the player block without potions");
            c.Equal("""[{"slot":0,"name":"Blood Potion","text":"Heal 20% of your Max HP.","target":"AnyPlayer","usable":true},null,null]""", Text(s.State["potions"]), "the detailed potions");
            c.Equal("open=Open the chest | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion", Labels(s), "open, then the potion ids");

            Do(run, "open");
            s = run.Snapshot();
            c.Equal("run,chest_open,relics,player,potions", Keys(s.State), "once open: the relics are listed");
            c.Equal(true, s.State["chest_open"]!.GetValue<bool>(), "the chest is open");
            c.Equal("""[{"n":0,"name":"Strike Dummy","text":"(the checks' stand-in for Strike Dummy)"}]""", Text(s.State["relics"]), "the one relic of a single player's chest");
            c.Equal(167, s.State["player"]!["gold"]!.GetValue<int>(), "the gold is paid already");
            c.Equal("relic:0=Take relic Strike Dummy | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion", Labels(s),
                "the relic, then the potion ids; proceed is never offered while an unpicked relic is listed");

            Do(run, "relic:0");
            s = run.Snapshot();
            c.Equal("run,chest_open,picked,relic_awarded,player,potions", Keys(s.State), "after the pick: picked and relic_awarded, and the relic is listed no more");
            c.Equal("Strike Dummy", s.State["picked"]!.GetValue<string>(), "picked names the relic");
            c.Equal(true, s.State["relic_awarded"]!.GetValue<bool>(), "the award is at once");
            c.Equal("""["Burning Blood","Strike Dummy"]""", Text(s.State["player"]!["relics"]), "the player's relics");
            c.Equal("proceed=Leave the treasure room | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion", Labels(s), "proceed, then the potion ids");

            Do(run, "potion:0");
            // Blood Potion: 20% of 80 is 16.
            c.Equal(54, run.Hp, "a potion can be used from the treasure room: 38 + 16");
            c.Equal("proceed", Ids(run.Snapshot()), "and the room is as it was");
        });
    }

    // ---------- 5. an empty chest ----------

    private static void EmptyChest(Check c)
    {
        c.Group("Silver Crucible: the first chest is empty, the second is a chest like any other", () =>
        {
            var setup = RealSetup(() => new SilverCrucible());
            var director = new Vault { Before = RealHistory };
            var run = new Arena.Run(setup, director);
            Do(run, "enter");
            c.Equal(0, Counter(run, RunStream.TreasureRoomRelics), "no rarity is rolled for a chest without treasure");
            c.That(World(run).SharedBag.Contains("StrikeDummy"), "and nothing is pulled from the bag");
            c.That(Chest(run).Relic is null && Chest(run).GameRelic is null && Chest(run).RolledRarity == RelicRarity.None, "the chest holds no relic");
            c.Equal("open", Ids(run.Snapshot()), "the chest is opened all the same");

            Do(run, "open");
            var s = run.Snapshot();
            c.Equal((116, RewardsBeforeTheChest), (run.Gold, Counter(run, RunStream.Rewards)), "no gold, and no number of rewards");
            c.Equal("run,chest_open,player,potions", Keys(s.State), "no relics, no picked");
            c.Equal(true, s.State["chest_open"]!.GetValue<bool>(), "the chest is open");
            c.Equal("proceed=Leave the treasure room (no relic on offer)", Labels(s), "proceed with the mod's label for an empty chest");
            var refused = run.Execute("relic:0");
            c.Equal("The game isn't taking relic votes right now.", refused.Error, "there is no relic to take");

            Do(run, "proceed", "enter");
            c.Equal(11, run.State.TotalFloor, "the next treasure room");
            c.Equal(2, run.State.Part<TreasureState>().RoomsEntered, "the second of the run");
            // The first chest drew nothing, so this one gets the stream's first number and the bag's front.
            c.Equal((1, "StrikeDummy"), (Counter(run, RunStream.TreasureRoomRelics), Chest(run).GameRelic), "the second chest holds the relic the first would have held");
            Do(run, "open");
            c.Equal((167, RewardsBeforeTheChest + 1), (run.Gold, Counter(run, RunStream.Rewards)), "and pays the gold the first would have paid");
            Do(run, "relic:0", "proceed");
            c.Equal("Burning Blood, Silver Crucible, Strike Dummy", RelicNames(run), "the relic is taken");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, director, run.Actions).Dump(), "both rooms replay from the save");
        });
    }

    // ---------- 6. rewards a relic adds to the room ----------

    private static void ExtraRewards(Check c)
    {
        c.Group("rewards a relic adds to a treasure room come after the gold and before the relic", () =>
        {
            var run = RealRun(null, () => new Bonus());
            Do(run, "enter");
            c.Equal("", run.State.FindRelic<Bonus>()!.Heard, "the room's rewards are not asked for on entering");
            Do(run, "open");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "the rewards screen lies over the treasure room");
            c.Equal("""[{"n":0,"kind":"gold","amount":7}]""", Text(s.State["rewards"]), "the relic's 7 gold");
            c.Equal("reward:0=Take 7 gold | proceed=Skip (leave without the unclaimed rewards)", Labels(s), "a screen that is no room's last: its proceed is Skip");
            c.Equal("rewards of Treasure,told,", run.State.FindRelic<Bonus>()!.Heard, "the hook was handed the Treasure room, and the relic told that it changed the rewards");
            c.Equal(167, run.Gold, "the chest's own gold was paid first, at once");
            // A gold row of a fixed amount that a hook adds is filled already and draws nothing.
            c.Equal(RewardsBeforeTheChest + 1, Counter(run, RunStream.Rewards), "one number of rewards in all: the chest's");

            Do(run, "reward:0");
            s = run.Snapshot();
            c.Equal("treasure", s.Screen, "the last row taken closes the rewards and shows the chest");
            c.Equal(174, run.Gold, "167 + 7");
            c.Equal("relic:0=Take relic Strike Dummy", Labels(s), "the relic comes after the rewards");
            Do(run, "relic:0", "proceed");
            c.Equal(1, ((Vault)run.Director).RoomsLeft, "and the room is left as ever");
        });

        c.Group("skipping the room's rewards shows the relic just the same", () =>
        {
            var run = RealRun(null, () => new Bonus());
            Do(run, "enter", "open", "proceed");
            c.Equal("treasure", run.Snapshot().Screen, "Skip closes the rewards");
            c.Equal(167, run.Gold, "the 7 gold was left behind");
            c.Equal("relic:0", Ids(run.Snapshot()), "the relic is on offer");
        });

        c.Group("an empty chest still offers the rewards a relic adds", () =>
        {
            var run = RealRun(null, () => new SilverCrucible(), () => new Bonus());
            Do(run, "enter", "open");
            c.Equal("rewards", run.Snapshot().Screen, "the rewards are generated whether or not there is treasure");
            Do(run, "reward:0");
            c.Equal((123, "proceed=Leave the treasure room (no relic on offer)"), (run.Gold, Labels(run.Snapshot())), "116 + 7, and an empty chest behind them");
        });
    }

    // ---------- 7. relics the arena has not built ----------

    private static void RelicsNotBuilt(Check c)
    {
        c.Group("the shared bag of the checks' seed starts its Common list as the checks assume", () =>
        {
            var run = RealRun();
            c.Sequence(["StrikeDummy", "AmethystAubergine", "FestivePopper"], World(run).SharedBag.Relics(RelicRarity.Common).Take(3), "the first three");
        });

        c.Group("the game's relic is not built: the next built one of the list is in the chest, and the log says what was passed over", () =>
        {
            var run = RealRun(new Vault { Before = RealHistory, IsBuilt = static name => name == "FestivePopper" });
            Do(run, "enter");
            c.Equal("StrikeDummy", Chest(run).GameRelic, "what the game gives");
            c.Equal("FestivePopper", Chest(run).Relic?.ClassName, "what the arena gives: the third of the list, the first it holds");
            var log = run.State.Part<LeftOutLog>();
            c.Sequence(["10:relic:StrikeDummy:a chest's relic", "10:relic:AmethystAubergine:a chest's relic"],
                log.Entries.Select(e => $"{e.Floor}:{e.Kind}:{e.ClassName}:{e.What}"), "both relics passed over are in the run's log, with the floor");
            c.Equal(10, log.FirstFloor, "the floor from which the run is no longer the game's own");
            c.Equal(1, Counter(run, RunStream.TreasureRoomRelics), "the rarity was rolled once: the list still had a built relic");
            c.That(World(run).SharedBag.Contains("StrikeDummy") && World(run).SharedBag.Contains("AmethystAubergine"), "the relics passed over stay in the bag");
            c.That(!World(run).SharedBag.Contains("FestivePopper"), "the one in the chest has left it");
            Do(run, "open", "relic:0");
            c.Equal("Burning Blood, Festive Popper", RelicNames(run), "and it can be taken");
        });

        c.Group("no built relic of the rolled rarity: the rarity is rolled again on the same stream, and the log has the game's relic", () =>
        {
            var probe = RealRun();
            var uncommon = World(probe).SharedBag.Relics(RelicRarity.Uncommon)[0];
            // A second reading: the stream is rolled until it gives Uncommon, the one rarity with a built relic.
            var stream = World(probe).Streams.Long(RunStream.TreasureRoomRelics).Clone();
            var rolls = 1;
            c.Equal(RelicRarity.Common, RunRelics.RollRarity(stream), "the first roll is Common, as the real chest's was");
            while (RunRelics.RollRarity(stream) != RelicRarity.Uncommon) rolls++;
            rolls++;

            var run = RealRun(new Vault { Before = RealHistory, IsBuilt = name => name == uncommon });
            Do(run, "enter");
            c.Equal("StrikeDummy", Chest(run).GameRelic, "what the game gives");
            c.Equal(RelicRarity.Common, Chest(run).RolledRarity, "and the rarity its roll gave");
            c.Equal(uncommon, Chest(run).Relic?.Name, "what the arena gives: the front of the Uncommon list, the one relic it holds");
            c.Equal(rolls, Counter(run, RunStream.TreasureRoomRelics), "every roll is a number of treasure_room_relics");
            c.Sequence(["10:relic:StrikeDummy:a chest's relic"], run.State.Part<LeftOutLog>().Entries.Select(e => $"{e.Floor}:{e.Kind}:{e.ClassName}:{e.What}"),
                "the log says that the game's relic was passed over, so the run is known not to be the game's own from here");
            c.That(World(run).SharedBag.Contains("StrikeDummy") && !World(run).SharedBag.Contains(uncommon), "Strike Dummy stays in the bag; the relic in the chest has left it");
        });

        c.Group("strict: a chest whose relic is not built ends the run as unrepresentable, standing as before the room", () =>
        {
            var director = new Vault { Before = RealHistory, Strict = true, IsBuilt = static name => name == "FestivePopper" };
            var setup = RealSetup();
            var run = new Arena.Run(setup, director);
            var before = run.State.Dump().ToJsonString();
            Do(run, "enter");
            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "the run ends by itself");
            c.That(run.ResultReason is { } reason && reason.Contains("the chest on floor 10", StringComparison.Ordinal) && reason.Contains("StrikeDummy", StringComparison.Ordinal),
                $"the reason names the chest and the relic ({run.ResultReason})");
            c.Equal(before, run.State.Dump().ToJsonString(), "nothing of the state changed: floor 9, no number drawn, the bag untouched");
            c.Sequence(["enter"], run.Actions, "the action stays in the save");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, director, run.Actions).Dump(), "which replays to the same end");
        });

        c.Group("strict with the game's relic built: the chest is the game's own", () =>
        {
            var run = RealRun(new Vault { Before = RealHistory, Strict = true, IsBuilt = static name => name == "StrikeDummy" });
            Do(run, "enter", "open", "relic:0", "proceed");
            c.That(!run.IsOver, "the run goes on");
            c.Equal(0, run.State.Part<LeftOutLog>().Entries.Count, "nothing was passed over");
        });

        c.Group("no relic of any rarity is built: the run ends as unrepresentable, strict or not", () =>
        {
            var run = RealRun(new Vault { Before = RealHistory, IsBuilt = static _ => false });
            Do(run, "enter");
            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "the run ends");
            c.That(run.ResultReason is { } reason && reason.StartsWith("the chest on floor 10: ", StringComparison.Ordinal), $"with the pull's reason ({run.ResultReason})");
        });
    }

    // ---------- 8. the first chest of a profile's first run ----------

    private static void FirstRunChest(Check c)
    {
        // A profile that has finished no run; everything else as unlocked as can be.
        var firstRun = new RunUnlocks(GameLists.Epochs, GameLists.Acts.SelectMany(a => a.Encounters).Select(e => e.ClassName), 0, GameLists.Acts.Select(a => a.ClassName));

        c.Group("the first chest of a profile's very first run holds Gorget, whatever the roll says", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = RealSeed }, new Vault { Unlocks = firstRun });
            var front = World(run).SharedBag.Relics(RelicRarity.Common)[0];
            c.That(front != "Gorget" && World(run).SharedBag.Contains("Gorget"), "Gorget is in the shared bag and not at the front of the list the roll lands on");
            Do(run, "enter");
            c.Equal("Gorget", Chest(run).GameRelic, "the game's relic");
            c.Equal("Gorget", Chest(run).Relic?.ClassName, "in the chest");
            c.Equal(1, Counter(run, RunStream.TreasureRoomRelics), "the rarity is rolled all the same, and not used");
            c.That(!World(run).SharedBag.Contains("Gorget"), "Gorget has left the shared bag");
            c.That(World(run).SharedBag.Contains(front), "and nothing was pulled from the list");

            Do(run, "open", "relic:0", "proceed", "enter");
            // The second chest of that run is a chest like any other: the stream's second number, the bag as it stands.
            var probe = World(run).Streams.Long(RunStream.TreasureRoomRelics).Clone();
            c.Equal(2, probe.Counter, "the second chest drew the second number");
            c.That(Chest(run).GameRelic != "Gorget" && Chest(run).GameRelic is not null, "and holds a relic pulled from the bag");
            c.Equal(Chest(run).RolledRarity, GameLists.BagRelic(Chest(run).GameRelic!).Rarity, "of the rarity it rolled");
        });

        c.Group("a profile with runs behind it gets no Gorget", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = RealSeed }, new Vault());
            Do(run, "enter");
            c.Equal("StrikeDummy", Chest(run).GameRelic, "the roll and the bag decide");
        });

        c.Group("the first run's Gorget is not built: the run ends as unrepresentable", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = RealSeed }, new Vault { Unlocks = firstRun, IsBuilt = static name => name != "Gorget" });
            Do(run, "enter");
            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "the run ends");
            c.That(run.ResultReason is { } reason && reason.Contains("Gorget", StringComparison.Ordinal), $"and the reason names Gorget ({run.ResultReason})");
        });
    }

    // ---------- 9. keyed streams ----------

    private static void KeyedStreams(Check c)
    {
        c.Group("keyed streams: the chest of a map point is the same whatever the run drew before, and the long streams stay still", () =>
        {
            // Two players on the same seed and the same map point; one of them has a longer history behind him.
            var plain = new Arena.Run(new RunSetup { Seed = RealSeed, Gold = 0 }, new Vault { Mode = StreamMode.Keyed });
            var other = new Arena.Run(new RunSetup { Seed = RealSeed, Gold = 0 }, new Vault
            {
                Mode = StreamMode.Keyed,
                Before = static run =>
                {
                    World(run).Streams.FastForward(RunStream.Rewards, 37);
                    World(run).Streams.FastForward(RunStream.TreasureRoomRelics, 5);
                },
            });
            Do(plain, "enter", "open");
            Do(other, "enter", "open");
            c.Equal(Chest(plain).RolledRarity, Chest(other).RolledRarity, "the same rarity roll");
            c.Equal(plain.Gold, other.Gold, "the same gold");
            c.That(plain.Gold is >= TreasureRoom.MinGold and <= TreasureRoom.MaxGold, $"between 42 and 52 ({plain.Gold})");
            c.Equal((0, 0), (Counter(plain, RunStream.Rewards), Counter(plain, RunStream.TreasureRoomRelics)), "the long streams gave nothing");
            c.Equal((37, 5), (Counter(other, RunStream.Rewards), Counter(other, RunStream.TreasureRoomRelics)), "nor did the other run's");

            // A second reading: each roll is the first number of the stream its place and its purpose name.
            var seed = World(plain).Streams.Seed;
            var place = new RunPlace(0, 9, 3);
            c.Equal(place, World(plain).Streams.Place, "the place the streams were told");
            c.Equal(RunRelics.RollRarity(new GameRng(RunStreams.KeyedSeed(seed, place, Purpose.ChestRarity))), Chest(plain).RolledRarity,
                "the rarity comes from the stream of the purpose chest_rarity");
            c.Equal(new GameRng(RunStreams.KeyedSeed(seed, place, Purpose.ChestGold)).NextInt(42, 53), plain.Gold,
                "the gold from the stream of the purpose chest_gold");

            // Another map point of the same row is another chest, each by its own two streams.
            var golds = new HashSet<int>();
            var differ = new List<string>();
            for (var column = 0; column < 7; column++)
            {
                var elsewhere = new Arena.Run(new RunSetup { Seed = RealSeed, Gold = 0 }, new Vault { Mode = StreamMode.Keyed, Column = column });
                Do(elsewhere, "enter", "open");
                golds.Add(elsewhere.Gold);
                var there = new RunPlace(0, 9, column);
                var rarity = RunRelics.RollRarity(new GameRng(RunStreams.KeyedSeed(seed, there, Purpose.ChestRarity)));
                var gold = new GameRng(RunStreams.KeyedSeed(seed, there, Purpose.ChestGold)).NextInt(42, 53);
                if (Chest(elsewhere).RolledRarity != rarity || elsewhere.Gold != gold)
                    differ.Add($"column {column}: {Chest(elsewhere).RolledRarity} and {elsewhere.Gold} gold, expected {rarity} and {gold}");
            }
            c.Equal("", string.Join("; ", differ), "on each of the seven columns the rarity and the gold are the first numbers of the chest_rarity and chest_gold streams of that place");
            c.That(golds.Count > 1, "the place decides the stream");
            var asked = World(plain).Streams.Dump()["asked"]!.AsObject().Select(entry => entry.Key).ToList();
            c.That(asked.Count == 2 && asked.Any(key => key.Contains("chest_rarity", StringComparison.Ordinal)) && asked.Any(key => key.Contains("chest_gold", StringComparison.Ordinal)),
                $"two keyed streams were asked for in the room, one per purpose ({string.Join("; ", asked)})");
        });
    }

    // ---------- 10. refusals ----------

    private static void Refusals(Check c)
    {
        c.Group("what the treasure room refuses, in the mod's words; a refusal changes nothing", () =>
        {
            var run = RealRun();
            Do(run, "enter");
            void Refused(string id, string error)
            {
                var before = run.Dump();
                var result = run.Execute(id);
                c.That(!result.Ok, $"'{id}' is refused");
                c.Equal(error, result.Error, $"the reason for '{id}'");
                c.Equal(before, run.Dump(), $"'{id}' changed nothing");
            }

            // The chest is shut.
            Refused("relic:0", "The relics are still appearing.");
            Refused("proceed", "The relic hasn't been awarded yet; leaving now would lose it.");
            Refused("skip", "Unknown treasure room action 'skip'.");
            Refused("relic:", "Unknown treasure room action 'relic:'.");
            Refused("potion:0", "'potion:0' is not available now.");

            // Open, the relic not taken.
            Do(run, "open");
            Refused("open", "The chest was already clicked once; waiting for it to open.");
            Refused("relic:1", "There is no relic 1.");
            Refused("proceed", "The relic hasn't been awarded yet; leaving now would lose it.");

            // The relic taken.
            Do(run, "relic:0");
            Refused("relic:0", "Already picked Strike Dummy.");
            Refused("open", "The chest was already clicked once; waiting for it to open.");
            c.Sequence(["enter", "open", "relic:0"], run.Actions, "no refused action is in the save");
        });

        c.Group("a treasure room needs the run's world", () =>
        {
            var run = Outside();
            var threw = false;
            try { TreasureRoom.Enter(run); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "entering one in a run without bags and streams is refused by name");
        });
    }

    // ---------- 11. on the map ----------

    /// <summary>An act walked on its real map: the chest and the rest sites are the built rooms, everything else is stood in for by a room that can only be left.</summary>
    private sealed class Walk : RunDirector, IMapDirector
    {
        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State);
            var map = MapState.BeginAct(run, 0);
            var arrival = map.Enter(run, map.Map.Start.Coord);
            run.AfterRoomEntered(arrival.Room);
            MapState.MarkRoomVisited(run, arrival.Room);
            run.Push(new MapScreen());
        }

        public void EnterRoom(Arena.Run run, MapArrival arrival)
        {
            MapState.MarkRoomVisited(run, arrival.Room);
            switch (arrival.Room)
            {
                case RoomKind.Treasure:
                    TreasureRoom.Enter(run, static _ => true, static name => new Loot(name));
                    break;
                case RoomKind.RestSite:
                    RestSite.Enter(run);
                    break;
                default:
                    // A fight's room is not announced by the run; the others are.
                    if (!arrival.Room.IsCombat()) run.AfterRoomEntered(arrival.Room);
                    run.Push(new Elsewhere());
                    break;
            }
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => throw new NotSupportedException("No fight is part of these checks.");

        public override void AfterRoom(Arena.Run run)
        {
            run.CloseAll();
            run.Push(new MapScreen());
        }
    }

    private sealed class Elsewhere : RunScreen
    {
        public override string Name => "event";

        public override GameSnapshot Snapshot(Arena.Run run) => new()
        {
            Screen = Name, AwaitingDecision = true, State = new JsonObject { ["run"] = run.RunJson() }, Actions = [new LegalAction("leave", "Leave")],
        };

        public override ActionResult Execute(Arena.Run run, string actionId)
        {
            if (actionId != "leave") return ActionResult.Fail($"Unknown event action '{actionId}'.");
            run.Pop();
            run.RoomFinished();
            return ActionResult.Success;
        }

        public override RunScreen Clone(CloneMap map) => new Elsewhere();
    }

    private static void OnTheMap(Check c)
    {
        c.Group("on a real map (seed 1BUSLN0FY5): the Treasure point of row 9 opens the chest on floor 10, the Rest Site of row 15 the rest site on floor 16", () =>
        {
            var setup = new RunSetup { Seed = "1BUSLN0FY5", Hp = 30 };
            var director = new Walk();
            var run = new Arena.Run(setup, director);
            var chestsOnRow9 = 0;
            var restsOnRow15 = 0;
            for (var step = 0; step < 200; step++)
            {
                var s = run.Snapshot();
                var map = run.State.Part<MapState>();
                var here = map.Current!.Value;
                if (s.Screen == "map")
                {
                    if (map.Next(run).Count == 1 && map.Next(run)[0].Type == MapPointType.Boss) break;
                    Do(run, "map:0");
                }
                else if (s.Screen == "treasure")
                {
                    if (here.Row == 9)
                    {
                        chestsOnRow9++;
                        c.Equal(MapPointType.Treasure, map.CurrentPoint!.Type, "row 9 is the Treasure row");
                        c.Equal("""{"act":1,"floor":10,"ascension":0}""", Text(s.State["run"]), "the chest of row 9 is on floor 10");
                        c.That(s.State["boss"] is null, "the treasure screen never names the boss");
                        c.Equal(RoomKind.Treasure, run.Room, "the room the hooks were told about");
                    }
                    var gold = run.Gold;
                    var relics = run.State.Relics.Count;
                    Do(run, "open", "relic:0");
                    c.That(run.Gold - gold is >= 42 and <= 52, $"the chest pays 42 to 52 gold ({run.Gold - gold})");
                    c.Equal(relics + 1, run.State.Relics.Count, "and gives one relic");
                    Do(run, "proceed");
                    c.Equal("map", run.Snapshot().Screen, "proceed opens the map");
                    c.Equal($"{here} {MapScreen.TypeWord(run.State.Part<MapState>().Map.Point(here)!.Type)}", run.Snapshot().State["at"]!.GetValue<string>(), "standing on the chest's point");
                }
                else if (s.Screen == "rest")
                {
                    c.Equal("Vantom", s.State["boss"]!.GetValue<string>(), "the rest site names the act's boss");
                    c.Equal(RoomKind.RestSite, run.Room, "the room the hooks were told about");
                    if (here.Row == 15)
                    {
                        restsOnRow15++;
                        c.Equal("""{"act":1,"floor":16,"ascension":0}""", Text(s.State["run"]), "the rest site of row 15 is on floor 16");
                    }
                    var hp = run.Hp;
                    Do(run, "option:0", "proceed");
                    c.Equal(Math.Min(80, hp + 24), run.Hp, "a rest heals 24 of 80");
                    c.Equal("map", run.Snapshot().Screen, "proceed opens the map");
                }
                else
                {
                    Do(run, "leave");
                }
            }

            c.Equal(1, chestsOnRow9, "the path met the chest of row 9");
            c.Equal(1, restsOnRow15, "and the rest site of row 15");
            c.Equal("(3,16) boss", string.Join(" ", run.State.Part<MapState>().Next(run).Select(p => $"{p.Coord} {MapScreen.TypeWord(p.Type)}")), "the boss is the one way on");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, director, run.Actions).Dump(), "the walk replays from its save");
        });
    }

    private static void RandomWalks(Check c)
    {
        c.Group("a random player on 80 real maps: every rest site and chest keeps the rooms' rules, and every walk replays", () =>
        {
            var problems = new List<string>();
            int rests = 0, smiths = 0, backedOut = 0, chests = 0;
            for (var n = 0; n < 80; n++)
            {
                var setup = new RunSetup { Seed = $"WALK{n:000}", Hp = 20, Gold = 0 };
                var director = new Walk();
                var run = new Arena.Run(setup, director);
                var dice = new GameRng((ulong)(1000 + n));
                void Wrong(string what) => problems.Add($"WALK{n:000} floor {run.State.TotalFloor}: {what}");

                // What the room the player stands in must add up to when it is left.
                string room = "";
                int hp = 0, gold = 0, relics = 0, upgrades = 0;
                bool rested = false, smithed = false;
                int Upgrades() => run.State.Deck.Sum(card => card.UpgradeLevel);

                for (var step = 0; step < 600; step++)
                {
                    var s = run.Snapshot();
                    if (!s.AwaitingDecision || s.Actions.Count == 0) { Wrong($"no decision on {s.Screen}"); break; }
                    var ids = s.Actions.Select(a => a.Id).ToList();

                    if (s.Screen is "rest" or "treasure" && room != s.Screen + run.State.TotalFloor)
                    {
                        room = s.Screen + run.State.TotalFloor;
                        (hp, gold, relics, upgrades, rested, smithed) = (run.Hp, run.Gold, run.State.Relics.Count, Upgrades(), false, false);
                    }
                    if (s.Screen == "rest")
                    {
                        var used = s.State["chosen"] is not null;
                        if (ids.Contains("proceed") != used) Wrong($"proceed offered {ids.Contains("proceed")} with an option used {used}");
                        if (used && s.State["options"]!.AsArray().Count != 0) Wrong("options left after one was used");
                        if (!used && (run.Hp != hp || Upgrades() != upgrades)) Wrong("the player changed before an option was used");
                    }
                    if (s.Screen == "treasure" && ids.Contains("proceed") && (s.State["relics"] is not null || !s.State["chest_open"]!.GetValue<bool>()))
                        Wrong("proceed offered with the chest shut or its relic not taken");
                    if (s.Screen == "map")
                    {
                        var next = run.State.Part<MapState>().Next(run);
                        if (next.Count == 1 && next[0].Type == MapPointType.Boss) break;
                    }

                    var id = ids[dice.NextInt(0, ids.Count)];
                    var screen = s.Screen;
                    var result = run.Execute(id);
                    if (!result.Ok) { Wrong($"'{id}' was offered on {screen} and refused: {result.Error}"); break; }

                    if (screen == "rest" && id == "option:0") { rested = true; rests++; }
                    if (screen == "rest" && id == "option:1") smiths++;
                    if (screen == "card_select" && id == "confirm") smithed = true;
                    if (screen == "card_select" && id == "cancel" && run.Snapshot().Screen == "rest") backedOut++;
                    if (id != "proceed") continue;
                    if (screen == "rest")
                    {
                        // 30% of 80 is 24; a rest heals that or what was missing, Smith upgrades one card, and each visit does one of the two.
                        if (rested == smithed) Wrong($"a visit with Rest {rested} and Smith {smithed}");
                        if (run.Hp != (rested ? Math.Min(80, hp + 24) : hp)) Wrong($"HP {hp} became {run.Hp} (rested {rested})");
                        if (Upgrades() != upgrades + (smithed ? 1 : 0)) Wrong($"{Upgrades() - upgrades} upgrades (smithed {smithed})");
                    }
                    if (screen == "treasure")
                    {
                        chests++;
                        if (run.Gold - gold is < 42 or > 52) Wrong($"a chest paid {run.Gold - gold} gold");
                        if (run.State.Relics.Count != relics + 1) Wrong($"a chest gave {run.State.Relics.Count - relics} relics");
                    }
                }

                if (run.Snapshot().Screen != "map" || run.State.TotalFloor != 16) Wrong($"the walk ended on {run.Snapshot().Screen}");
                if (run.Dump() != Arena.Run.Replay(setup, director, run.Actions).Dump()) Wrong("the walk does not replay");
            }
            c.Equal("", string.Join("; ", problems.Take(5)), "no rule broken on any map");
            c.That(rests > 50 && smiths > 50 && backedOut > 5 && chests >= 80, $"the walks rested {rests} times, opened Smith {smiths} times, backed out of it {backedOut} times and opened {chests} chests");
        });
    }

    // ---------- 12. replay and copies ----------

    private static void ReplayAndCopy(Check c)
    {
        c.Group("a copy in the middle of the room goes on as the run does", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, Hp = 38, Gold = 116, Potions = ["BloodPotion"], ExtraRelics = [() => new Bonus(), () => new Witness()] };
            var director = new Vault { Before = RealHistory };
            var run = new Arena.Run(setup, director);
            Do(run, "enter");
            var atTheDoor = run.Clone();
            c.Equal(run.Dump(), atTheDoor.Dump(), "a copy with the chest shut is in the same state");
            c.That(!ReferenceEquals(Chest(run).Relic, Chest(atTheDoor).Relic), "with a relic object of its own in its chest");

            Do(run, "open");
            var atTheRewards = run.Clone();
            Do(run, "reward:0", "relic:0", "potion:0", "proceed");
            Do(atTheDoor, "open", "reward:0", "relic:0", "potion:0", "proceed");
            Do(atTheRewards, "reward:0", "relic:0", "potion:0", "proceed");
            c.Equal(run.Dump(), atTheDoor.Dump(), "the copy taken before the chest was opened ends in the same state");
            c.Equal(run.Dump(), atTheRewards.Dump(), "and so does the copy taken with the rewards up");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, director, run.Actions).Dump(), "the room replays from its action list");
            // 116 + 51 + 7 gold; 38 + 16 HP.
            c.Equal((174, 54), (run.Gold, run.Hp), "the numbers of the room");
        });
    }
}
