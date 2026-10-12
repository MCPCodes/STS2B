using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// The three ways to run an act (plan 5.1, "three ways to run, one switch"). They differ in what
/// happens when a roll lands on something the arena has not built, and in which seeds a run may
/// start on; the rules of the game are the same in all three.
/// </summary>
public enum RunMode
{
    /// <summary>
    /// Any seed whose first act is Overgrowth. A roll that lands on something unbuilt is drawn
    /// again by the house rule of its part and written to the run's log
    /// (<see cref="LeftOutLog"/>); the run ends as unrepresentable only where no redraw can help.
    /// For the scripted baselines and soak runs.
    /// </summary>
    DrawAgain,

    /// <summary>
    /// The same rules, on a seed that passes the director's seed screen
    /// (<see cref="ActDirector.Screen"/>): the run begins as the game's own run for its seed. A
    /// seed that fails the screen is refused at the start (the run ends as unrepresentable with
    /// the screen's reason). The default for runs a model plays.
    /// </summary>
    Screened,

    /// <summary>
    /// The game's streams with nothing drawn again: a run that meets anything unbuilt ends as
    /// unrepresentable. Every run that finishes is exactly the run the game gives for that seed
    /// and those actions. Used to measure the share of real runs the build can represent.
    /// </summary>
    Strict,
}

/// <summary>
/// The first act as the game lays it out, from Neow's room to the proceed of the boss's
/// rewards (plan 1.2, 1.4, 1.5): the director of a run that is a whole act.
/// <code>
/// var run = ActRun.Start("1BUSLN0FY5");                 // or: new Run(ActRun.Setup(seed), new ActDirector { Mode = RunMode.Strict })
/// run.Snapshot();                                       // Neow's three relics
/// run.Execute("option:1");                              // every screen and id is the mod's
/// </code>
/// What it does, in the game's order:
/// <list type="bullet">
/// <item><b>The start</b> (<see cref="Begin"/>): the run's world from the seed (the act roll, the
/// bags, the lists, the streams), the strict switch, the map of the act, and the start point
/// entered at once with no map decision: Neow's room, floor 1. Her room is an Event room, so
/// the act's event counter moves once she is in it.</item>
/// <item><b>A point is entered</b> (<see cref="EnterRoom"/>, called by the map screen once the
/// path, the floor and an Unknown point's roll are done): a Monster, Elite or Boss room takes
/// the act's next list entry and starts the fight; a shop, a rest site and a treasure room open
/// through their own entry points; an Event room takes the event the list walk gives. Then the
/// act's counter for that kind of room moves.</item>
/// <item><b>A fight is won</b> (<see cref="AfterFightWon"/>): its rewards screen, whose proceed
/// finishes the room; a fight that gives none finishes the room at once.</item>
/// <item><b>A room is finished</b> (<see cref="AfterRoom"/>): the map opens. On the boss's
/// point the act is cleared instead and the run ends there.</item>
/// </list>
/// It writes the floor log as it goes (<see cref="FloorLog"/>) and has its run keep the choices
/// (<see cref="Run.Choices"/>).
/// <para>
/// <b>What the arena has not built.</b> A seed whose first act is not Overgrowth is the game's
/// run of another act: it is refused at the start in every mode. Everything else follows the
/// mode (<see cref="RunMode"/>) through the parts' own house rules: an Event room that no built
/// event can fill is not rolled for an Unknown point (<see cref="CanHold"/>), a roll that lands
/// on something unbuilt is drawn again and logged (the Unknown point's second roll too: the
/// director writes the event the game would have met there), and whatever cannot be shown at all, or
/// anything unbuilt in strict mode, ends the run as unrepresentable
/// (<see cref="RunUnrepresentableException"/>, which the run catches itself). Nothing here knows
/// which relics or events exist: the registry decides.
/// </para>
/// <para>
/// House rules of the act, beside the parts' own: the run ends at the proceed of the boss's
/// rewards, where the game would go on to the second act's Ancient; the game's "after act
/// entered" hook, which it sends once Neow's room is entered, is not sent (only the run
/// modifier Cursed Run hears it).
/// </para>
/// </summary>
public sealed class ActDirector : RunDirector, IMapDirector
{
    /// <summary>How the run meets what is not built, and which seeds it starts on.</summary>
    public RunMode Mode { get; init; } = RunMode.DrawAgain;

    /// <summary>
    /// The game's long streams, or one stream per place and purpose (plan 5.4): keyed streams are
    /// the house rule for comparing players, and go only with <see cref="RunMode.DrawAgain"/>,
    /// since a strict or a screened run has to be the game's own.
    /// </summary>
    public StreamMode Streams { get; init; } = StreamMode.Game;

    /// <summary>The screen a seed has to pass in <see cref="RunMode.Screened"/>; unused in the other modes.</summary>
    public SeedScreen Screen { get; init; } = SeedScreen.Default;

    /// <summary>The profile the run is generated for; everything unlocked when null.</summary>
    public RunUnlocks? Unlocks { get; init; }

    /// <summary>The run keeps its choices for the floor log.</summary>
    public override bool KeepsChoices => true;

    // ---------- the start of the run ----------

    public override void Begin(Run run)
    {
        if (Streams == StreamMode.Keyed && Mode != RunMode.DrawAgain)
            throw new ArgumentException($"Keyed streams are a house rule for comparing players: they go with {nameof(RunMode.DrawAgain)}, not with a {Mode} run, which is the game's own.");

        var state = run.State;
        var world = RunWorld.Begin(state, Unlocks, Streams);
        // Before anything is rolled: Neow's offer reads the switch.
        if (Mode == RunMode.Strict) state.Part<LeftOutLog>().Strict = true;

        if (world.Acts[0] != GameLists.Overgrowth)
            throw new RunUnrepresentableException($"{ActRun.ActRefusal} {state.Seed} is {world.Acts[0]}; the arena has built {GameLists.Overgrowth} alone");
        if (Mode == RunMode.Screened && Screen.WhyNot(state.Seed, world.Unlocks) is { } reason)
            throw new RunUnrepresentableException($"{ActRun.ScreenRefusal} ({Screen}): {reason}");

        // The game enters the start point at once (RunManager.EnterAct): no map decision before Neow.
        var map = MapState.BeginAct(run, 0);
        var arrival = map.Enter(run, map.Map.Start.Coord);
        var ancient = world.Rooms[0].Ancient ?? throw new InvalidOperationException($"The act {world.Acts[0]} has no Ancient for its start point.");
        OpenEvent(run, arrival, ancient);
    }

    // ---------- a map point is entered ----------

    public void EnterRoom(Run run, MapArrival arrival)
    {
        var state = run.State;
        if (_barredEvent is { } barred)
        {
            // The game rolled this Unknown point to be an Event room and the arena rolled again (CanHold): that is a
            // redraw like any other, so it goes into the run's log, on the floor of the room that was entered instead.
            _barredEvent = null;
            state.Part<LeftOutLog>().PassedOver(state, "event", barred, "an Unknown point's room, rolled again with the Event room barred");
        }

        var rooms = state.Part<RunWorld>().Rooms[state.ActIndex];
        switch (arrival.Room)
        {
            case RoomKind.Monster or RoomKind.Elite or RoomKind.Boss:
                // The act's next list entry of the room's kind; the list's counter moves once the room is entered.
                var encounter = arrival.Room switch
                {
                    RoomKind.Monster => rooms.NextNormalEncounter,
                    RoomKind.Elite => rooms.NextEliteEncounter,
                    _ => rooms.Boss,
                };
                FloorLog.Enter(run, arrival, encounter);
                run.StartFight(new FightRequest { Encounter = encounter });
                break;
            case RoomKind.Shop:
                FloorLog.Enter(run, arrival);
                ShopRoom.Enter(run);
                break;
            case RoomKind.RestSite:
                FloorLog.Enter(run, arrival);
                RestSite.Enter(run);
                break;
            case RoomKind.Treasure:
                FloorLog.Enter(run, arrival);
                TreasureRoom.Enter(run);
                break;
            case RoomKind.Event:
                // The walk of the act's event list: it moves the counter past what it steps over and marks the event met.
                OpenEvent(run, arrival, EventList.Pull(run));
                return;
            default:
                throw new InvalidOperationException($"The map point at {arrival.Coord} leads into a {arrival.Room} room, which no act has.");
        }
        MapState.MarkRoomVisited(run, arrival.Room);
    }

    /// <summary>An event's room, Neow's included: the floor's entry, the room, then the act's event counter.</summary>
    private static void OpenEvent(Run run, MapArrival arrival, string eventClassName)
    {
        var ev = Content.Registry.NewEvent(eventClassName);
        FloorLog.Enter(run, arrival, eventClassName, ev.Title);
        EventScreen.Open(run, ev);
        MapState.MarkRoomVisited(run, RoomKind.Event);
    }

    /// <summary>
    /// The house rule for an Unknown point (plan 5.1): it is not rolled to be an Event room while
    /// no event the arena has built is allowed and unmet. In strict mode the answer is always
    /// yes, and the Event room then ends the run if its event is not built. A no is followed by
    /// another roll of the same point (<see cref="MapState.Enter"/>); the event the game would
    /// have met there is kept until the room is entered and then written to the run's log.
    /// </summary>
    public bool CanHold(Run run, RolledRoom room)
    {
        if (room != RolledRoom.Event || EventList.CanHold(run)) return true;
        _barredEvent = EventList.Peek(run).GameEvent;
        return false;
    }

    /// <summary>The event of an Event room the arena barred while the point the player is travelling to was rolled; null between travels.</summary>
    private string? _barredEvent;

    // ---------- what follows a fight and a room ----------

    public override void AfterFightWon(Run run, FightOutcome fight)
    {
        FloorLog.FightWon(run, fight);
        // No rewards screen (an encounter that gives none): the room is finished as it stands.
        if (RunRewards.OfferForRoomEnd(run, fight) is null && !run.IsOver) AfterRoom(run);
    }

    public override void AfterRoom(Run run)
    {
        FloorLog.Leave(run);
        // The room on the boss's point was finished: the act is cleared. The map is never opened there.
        if (run.State.Part<MapState>().CurrentPoint?.Type == MapPointType.Boss)
        {
            run.End(RunResult.Cleared);
            return;
        }
        run.CloseAll();
        run.Push(new MapScreen());
    }

    public override RunDirector Clone(CloneMap map) => new ActDirector { Mode = Mode, Streams = Streams, Screen = Screen, Unlocks = Unlocks, _barredEvent = _barredEvent };
}

/// <summary>
/// Starting, saving and carrying on a run of the first act, and the seeds to start one on.
/// <code>
/// var run = ActRun.Start("1BUSLN0FY5", RunMode.Screened);            // the run alone: Snapshot, Execute, Clone
/// var game = ActRun.StartGame(seed, mode, actionLogPath: "run.actions");   // behind IGameBridge, the save written after every action
/// var again = ActRun.Resume(seed, mode, "run.actions");               // the same run rebuilt from its save, to be carried on
/// var then = ActRun.Replay(seed, mode, run.Actions.Take(40));         // the run as it stood after its first 40 actions
/// foreach (var seed in ActRun.Seeds(10, 1, RunMode.Screened)) ...     // ten seeds fit for a mode
/// </code>
/// A run is decided by its seed, its mode (with the stream mode and, for a screened run, the
/// screen) and its accepted action ids. The save holds the seed and the ids; whoever keeps a
/// save keeps the mode beside it and hands the same one to <see cref="Resume"/>.
/// </summary>
public static class ActRun
{
    /// <summary>What a run of the act starts with: the Ironclad as the game makes the player, on the seed as the game writes it.</summary>
    public static RunSetup Setup(string seed) => new() { Seed = RunSeed.Canonical(seed) };

    /// <summary>The act's director for a mode.</summary>
    /// <exception cref="ArgumentException">Keyed streams with a mode that has to be the game's own run.</exception>
    public static ActDirector Director(RunMode mode = RunMode.DrawAgain, StreamMode streams = StreamMode.Game, SeedScreen? screen = null)
    {
        if (streams == StreamMode.Keyed && mode != RunMode.DrawAgain)
            throw new ArgumentException($"Keyed streams go with {nameof(RunMode.DrawAgain)} only: a {mode} run is the game's own run and uses the game's streams.", nameof(streams));
        return new ActDirector { Mode = mode, Streams = streams, Screen = screen ?? SeedScreen.Default };
    }

    /// <summary>
    /// A new run of the act on a seed. Its first screen is Neow's, unless the seed is refused at
    /// the start (another first act; in screened mode a seed that fails the screen; in strict mode
    /// an offer of Neow's that is not built): the run is then over at once as unrepresentable,
    /// with the reason.
    /// </summary>
    public static Run Start(string seed, RunMode mode = RunMode.DrawAgain, StreamMode streams = StreamMode.Game, SeedScreen? screen = null) =>
        new(Setup(seed), Director(mode, streams, screen));

    /// <summary>
    /// The same behind the bridge the tools drive (<see cref="RunGame"/>). With
    /// <paramref name="actionLogPath"/> the run's save is written to that file after every
    /// accepted action; a file that is there already must be carried on with <see cref="Resume"/>
    /// instead, or deleted first.
    /// </summary>
    /// <param name="actionDelay">The busy spell after an action; zero for a scripted player, the arena's default when null.</param>
    public static RunGame StartGame(string seed, RunMode mode = RunMode.DrawAgain, StreamMode streams = StreamMode.Game, SeedScreen? screen = null,
        string? actionLogPath = null, TimeSpan? actionDelay = null, ExperimentOptions? experiments = null) =>
        new(Start(seed, mode, streams, screen))
        {
            ActionLogPath = actionLogPath, ActionDelay = actionDelay ?? ArenaGame.DefaultActionDelay, Experiments = experiments ?? new ExperimentOptions(),
        };

    /// <summary>
    /// A run rebuilt from its save file by replaying it, to be carried on: further actions are
    /// appended to the same file. The mode, the stream mode and the screen must be the ones the
    /// run was started with.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file is of another seed, or an action in it is refused (another mode, or a build that changed since).</exception>
    public static RunGame Resume(string seed, RunMode mode, string actionLogPath, StreamMode streams = StreamMode.Game, SeedScreen? screen = null,
        TimeSpan? actionDelay = null, ExperimentOptions? experiments = null) =>
        RunGame.Resume(Setup(seed), Director(mode, streams, screen), actionLogPath, actionDelay, experiments);

    /// <summary>
    /// A run built again from action ids, without a file: the run as it stood after those actions.
    /// With the first k actions of a finished run's save it is that run at any earlier point: the
    /// state a choice was made from (<see cref="RunChoice.Action"/> is a choice's place in the
    /// list), or the start of a fight, to be copied and played on another way.
    /// </summary>
    /// <exception cref="InvalidOperationException">An action is refused: the list is not of this seed and mode, or of another build.</exception>
    public static Run Replay(string seed, RunMode mode, IEnumerable<string> actions, StreamMode streams = StreamMode.Game, SeedScreen? screen = null) =>
        Run.Replay(Setup(seed), Director(mode, streams, screen), actions);

    // ---------- seeds ----------

    /// <summary>
    /// Seeds as the game makes them (ten letters and digits), one after the other from a maker
    /// seed: the same maker seed gives the same seeds. They are not looked at: about half start in
    /// Underdocks.
    /// </summary>
    public static IEnumerable<string> RandomSeeds(int makerSeed)
    {
        var maker = new GameRng(unchecked((ulong)makerSeed * 0x9E3779B97F4A7C15UL + 20261011UL));
        while (true) yield return RunSeed.Random(maker).Text;
    }

    /// <summary>
    /// The first <paramref name="count"/> seeds of <see cref="RandomSeeds"/> that a mode can use:
    /// every seed for a strict run (which measures real runs as they come), the seeds whose first
    /// act is Overgrowth for drawing again, and the seeds that pass the screen for a screened run.
    /// </summary>
    /// <param name="tried">How many seeds were looked at to find them.</param>
    /// <param name="mostTried">Where the search gives up; fewer seeds than asked for come back then.</param>
    public static List<string> Seeds(int count, int makerSeed, RunMode mode, out int tried, SeedScreen? screen = null, RunUnlocks? unlocks = null, int mostTried = 2_000_000)
    {
        unlocks ??= RunUnlocks.All;
        screen ??= SeedScreen.Default;
        var seeds = new List<string>(count);
        tried = 0;
        foreach (var seed in RandomSeeds(makerSeed))
        {
            if (seeds.Count >= count || tried >= mostTried) break;
            tried++;
            var fits = mode switch
            {
                RunMode.Strict => true,
                RunMode.DrawAgain => RunGeneration.RollActs(new RunSeed(seed), unlocks)[0] == GameLists.Overgrowth,
                // The act first: it is one draw, and half of all seeds end there.
                _ => RunGeneration.RollActs(new RunSeed(seed), unlocks)[0] == GameLists.Overgrowth && screen.Passes(seed, unlocks),
            };
            if (fits) seeds.Add(seed);
        }
        return seeds;
    }

    /// <summary><see cref="Seeds(int, int, RunMode, out int, SeedScreen?, RunUnlocks?, int)"/> without the count of seeds tried.</summary>
    public static List<string> Seeds(int count, int makerSeed, RunMode mode = RunMode.DrawAgain, SeedScreen? screen = null) =>
        Seeds(count, makerSeed, mode, out _, screen);

    // ---------- the result of a run ----------

    /// <summary>How the reason of a run begins that was refused because its seed's first act is not built.</summary>
    public const string ActRefusal = "the first act of seed";

    /// <summary>How the reason of a screened run begins whose seed fails the screen.</summary>
    public const string ScreenRefusal = "the seed does not pass the screen";

    /// <summary>
    /// What kind of thing ended a run as unrepresentable, read off its reason: "act" (another
    /// first act), "screen" (a seed the screen refused), "neow", "event", "relic", "card",
    /// "potion", or "other". For a tally; the reason itself says exactly what.
    /// </summary>
    public static string MissingKind(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return "other";
        bool Has(string text) => reason.Contains(text, StringComparison.Ordinal);
        // The two refusals of the start, in the director's own words.
        if (reason.StartsWith(ScreenRefusal, StringComparison.Ordinal)) return "screen";
        if (reason.StartsWith(ActRefusal, StringComparison.Ordinal)) return "act";
        // Neow's offer says so first; a roll that was not drawn again names the kind it landed on (LeftOutLog.PassedOver).
        if (reason.StartsWith("Neow", StringComparison.Ordinal) || Has("Neow's offer")) return "neow";
        foreach (var kind in new[] { "relic", "card", "potion", "event" })
            if (Has($"landed on the {kind} ")) return kind;
        if (Has("Event room")) return "event";
        if (Has("potion")) return "potion";
        if (Has("relic")) return "relic";
        if (Has("card")) return "card";
        return "other";
    }

    /// <summary>One line on a finished run, for a table: "cleared, 44/80 HP", "died on floor 9 (Elite)", "unrepresentable on floor 4: ...".</summary>
    public static string Outcome(Run run)
    {
        var state = run.State;
        return run.Result switch
        {
            RunResult.Cleared => string.Create(CultureInfo.InvariantCulture, $"cleared, {state.Hp}/{state.MaxHp} HP"),
            RunResult.Died => string.Create(CultureInfo.InvariantCulture, $"died on floor {state.TotalFloor}{(run.LastFight is { Won: false } fight ? $" ({fight.Encounter})" : "")}"),
            RunResult.Unrepresentable => string.Create(CultureInfo.InvariantCulture, $"unrepresentable on floor {state.TotalFloor}: {run.ResultReason}"),
            RunResult.Error => $"error: {run.ResultReason}",
            _ => "not finished",
        };
    }
}
