using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// What a look at an act's event list gave: the event the game takes for an Event room now, and
/// the event the arena opens.
/// </summary>
/// <param name="Event">The event the arena opens, by class name; null when it can open none (<see cref="Reason"/> says why).</param>
/// <param name="GameEvent">The event the game takes. The same as <see cref="Event"/> unless the arena has not built it.</param>
/// <param name="Counter">The act's event counter once the walk is done: it stands on the event taken.</param>
/// <param name="SteppedOver">The list entries the game's own walk stepped over because they were not allowed now or were met before, in order.</param>
/// <param name="PassedOver">Events that were allowed and unmet but that the arena has not built (the house rule), in order.</param>
/// <param name="Repeats">The game found no allowed, unmet event in a whole lap and takes the entry it stood on, allowed or not.</param>
/// <param name="Reason">For a look that found nothing to open: why, in one line.</param>
public sealed record EventPull(string? Event, string GameEvent, int Counter, IReadOnlyList<string> SteppedOver,
    IReadOnlyList<string> PassedOver, bool Repeats, string? Reason = null)
{
    public bool Found => Event is not null;

    /// <summary>The event is the one the game takes: nothing was passed over.</summary>
    public bool IsTheGames => Found && PassedOver.Count == 0;
}

/// <summary>
/// Which event an Event room holds (the game's ActModel.PullNextEvent and RoomSet): the next
/// entry of the act's event list, from the act's counter on, that is allowed right now and was
/// not met in this run.
/// <code>
/// // In a director's EnterRoom, for arrival.Room == RoomKind.Event:
/// EventScreen.Open(run, EventList.Pull(run));
/// MapState.MarkRoomVisited(run, RoomKind.Event);
/// // In its IMapDirector.CanHold:
/// room != RolledRoom.Event || EventList.CanHold(run)
/// </code>
/// The rules, as the game has them:
/// <list type="bullet">
/// <item>An act's list is its own events and all eighteen shared ones in one shuffle, made when
/// the run starts (<see cref="ActRooms.Events"/>).</item>
/// <item>The walk starts at the act's counter and tests entry after entry: allowed now
/// (<see cref="IsAllowed(Run, string)"/>) and not met before in this run. Every entry it steps
/// over moves the counter, for good. The list wraps at its end.</item>
/// <item>When a whole lap finds nothing, the game takes the entry the walk started on, allowed
/// or not ("allowing repetition").</item>
/// <item>The event taken is met from then on (<see cref="EventsState.Visited"/>). Entering the
/// room then moves the counter once more, as every room of type Event does: the Ancient's room
/// at the start of the act too, so the first Event room of an act starts its walk at entry 1.</item>
/// </list>
/// <para>
/// <b>Events the arena has not built</b> (a house rule; the plan's "draw again"). When the event
/// the game takes is not built, strict mode (<see cref="LeftOutLog.Strict"/>) ends the run as
/// unrepresentable. Otherwise the event is passed over, noted in the run's
/// <see cref="LeftOutLog"/>, and the walk goes on along the same lap for the next entry that is
/// allowed, unmet and built. When there is none, the room cannot be an Event room at all:
/// <see cref="CanHold"/> says so beforehand, and the map then rolls the Unknown point again with
/// the Event barred (<see cref="IMapDirector.CanHold"/>).
/// </para>
/// <para>
/// Left out, by name: the hook that swaps the next event (ModifyNextEvent, which only the card
/// Lantern Key of an act 2 event overrides); when the events of other acts are allowed
/// (Underdocks, Hive, Glory) and the three shared events whose own rule needs more than "not in
/// the first act" (Ranwid the Elder, Relic Trader, Fake Merchant: asked in a later act they are
/// refused by name).
/// </para>
/// </summary>
public static class EventList
{
    /// <summary>The thirteen events of the Overgrowth act, in the order of the game's list.</summary>
    public static readonly IReadOnlyList<string> Overgrowth =
    [
        "AromaOfChaos", "ByrdonisNest", "DenseVegetation", "JungleMazeAdventure", "LuminousChoir", "MorphicGrove", "SapphireSeed",
        "SunkenStatue", "TabletOfTruth", "UnrestSite", "Wellspring", "WhisperingHollow", "WoodCarvings",
    ];

    /// <summary>The eight shared events whose own rule lets them come up in a first act.</summary>
    public static readonly IReadOnlyList<string> SharedInAFirstAct =
    [
        "BrainLeech", "RoomFullOfCheese", "SelfHelpBook", "SlipperyBridge", "TeaMaster", "TheFutureOfPotions", "TheLegendsWereTrue", "ThisOrThat",
    ];

    /// <summary>The twenty-one events an Overgrowth first act can hold. The other ten of its list of thirty-one are shared
    /// events that are never allowed in a first act.</summary>
    public static readonly IReadOnlyList<string> InAnOvergrowthAct = Overgrowth.Concat(SharedInAFirstAct).ToList();

    /// <summary>The relics that bring a pet (AddsPet in their classes). Byrdonis Nest is not met by a player who has one.</summary>
    public static readonly IReadOnlySet<string> PetRelics = new HashSet<string>(StringComparer.Ordinal) { "Byrdpip", "PaelsLegion" };

    /// <summary>Whether the arena has built an event: it is registered, so its room can be opened.</summary>
    public static bool IsBuilt(string eventClassName) => Content.Registry.Has(ContentKind.Event, eventClassName);

    // ---------- when an event is allowed ----------

    /// <summary>
    /// Whether an event may come up right now (its IsAllowed in the game, single player), for
    /// every event of an Overgrowth act's list. Asked while a room is being entered or just
    /// after: the floor the events read is the game's, which does not count the room's own map
    /// point yet (<see cref="FloorsBehind"/>).
    /// </summary>
    /// <exception cref="NotSupportedException">The arena has no rule for that event, or for it in this act.</exception>
    public static bool IsAllowed(Run run, string eventClassName) => IsAllowed(run, eventClassName, FloorsBehind(run), peek: true);

    /// <summary>
    /// The total floor as the game's walk reads it: the number of map points entered before the
    /// one whose room is being made. The game counts a map point only after its room exists
    /// (it appends the point's history entry once the room is created), so an event that asks for
    /// "total floor above 6" (Slippery Bridge) is first allowed in a room on floor 8.
    /// </summary>
    public static int FloorsBehind(Run run)
    {
        var state = run.State;
        var map = state.FindPart<MapState>();
        // While a point is being entered (the Unknown roll asks IMapDirector.CanHold) it is on the
        // path but not counted; once MapState.Enter is done the run's total floor counts it.
        var counted = map is not { HasMap: true } || map.Visited.Count == map.History.Count;
        return counted ? state.TotalFloor - 1 : state.TotalFloor;
    }

    /// <param name="totalFloor">The game's total floor at the time of the walk (<see cref="FloorsBehind"/>).</param>
    /// <param name="peek">True leaves everything as it is. False is the walk itself: Luminous Choir asks the player's
    /// relic bag whether a relic is left, and that question drops relics the run no longer allows from the bag.</param>
    internal static bool IsAllowed(Run run, string eventClassName, int totalFloor, bool peek)
    {
        var state = run.State;
        var act = state.ActIndex;
        switch (eventClassName)
        {
            // ----- Overgrowth -----
            case "AromaOfChaos":
            case "JungleMazeAdventure":
            case "SapphireSeed":
            case "SunkenStatue":
            case "TabletOfTruth":
            case "Wellspring":
                return true;
            case "DenseVegetation":
                // With more than one player nobody may be at or below the HP it costs; alone it is always allowed.
                return true;
            case "ByrdonisNest":
                return !state.Relics.Any(relic => PetRelics.Contains(relic.ClassName)) && !state.Deck.Any(card => card.Def.ClassName == "ByrdonisEgg");
            case "LuminousChoir":
                return state.Gold >= 149 && RelicLeft(state, peek);
            case "MorphicGrove":
                return state.Gold >= 100 && state.Deck.Count(card => DeckEdits.IsTransformable(run, card)) >= 2;
            case "UnrestSite":
                return state.Hp <= state.MaxHp * 0.70m;
            case "WhisperingHollow":
                return state.Gold >= 44;
            case "WoodCarvings":
                return state.Deck.Any(card => card.Def.Rarity == CardRarity.Basic && DeckEdits.IsRemovable(card));

            // ----- shared, possible in a first act -----
            case "SelfHelpBook":
            case "ThisOrThat":
                return true;
            case "BrainLeech":
            case "RoomFullOfCheese":
                return act < 2;
            case "SlipperyBridge":
                return totalFloor > 6 && state.Deck.Any(DeckEdits.IsRemovable);
            case "TeaMaster":
                return act < 2 && state.Gold >= 150;
            case "TheFutureOfPotions":
                return state.Belt.Slots.Count(potion => potion is not null) >= 2;
            case "TheLegendsWereTrue":
                return act == 0 && state.Deck.Count > 0 && state.Hp >= 10;

            // ----- shared, never in a first act -----
            case "WarHistorianRepy":
                return false;
            case "PotionCourier":
            case "Symbiote":
                return act > 0;
            case "CrystalSphere":
                return state.Gold >= 100 && act > 0;
            case "DollRoom":
                return act == 1;
            case "StoneOfAllTime":
                return act == 1 && state.Belt.Slots.Any(potion => potion is not null);
            case "WelcomeToWongos":
                return act == 1 && state.Gold >= 100;
            case "RanwidTheElder":
            case "RelicTrader":
            case "FakeMerchant":
                if (act == 0) return false;
                throw new NotSupportedException($"The arena has no rule for when the event {eventClassName} is allowed after the first act: only a first act is in scope.");

            default:
                throw new NotSupportedException($"The arena has no rule for when the event {eventClassName} is allowed: only the events of an Overgrowth act's list are in scope.");
        }
    }

    /// <summary>Luminous Choir's second condition: a pull from the player's relic bag would find a relic (RelicGrabBag.HasAvailableRelics).</summary>
    private static bool RelicLeft(RunState state, bool peek)
    {
        var world = state.Part<RunWorld>();
        var bag = world.PlayerBag;
        if (peek)
        {
            // The question itself changes the bag (it drops what the run no longer allows): ask a copy,
            // brought up to date as a pull would bring the bag up to date first.
            bag = bag.Clone();
            foreach (var relic in state.Relics) bag.Remove(relic.ClassName);
        }
        else
        {
            RunRelics.SyncBags(state);
        }
        return bag.HasAvailableRelics(relic => RunRelics.IsAllowed(relic, state, world.Unlocks));
    }

    // ---------- the walk ----------

    /// <summary>
    /// The event an Event room entered now would hold, without changing anything: what
    /// <see cref="Pull"/> would give. For a director's <see cref="IMapDirector.CanHold"/>, and for
    /// screening seeds.
    /// </summary>
    /// <param name="isBuilt">Which events the arena holds; the registry when left out. For checks.</param>
    public static EventPull Peek(Run run, Func<string, bool>? isBuilt = null)
    {
        var state = run.State;
        var rooms = state.Part<RunWorld>().Rooms[state.ActIndex].Clone();
        return Walk(run, rooms, isBuilt ?? IsBuilt, peek: true);
    }

    /// <summary>
    /// Whether the arena can open an Event room now: always in strict mode (the run is then the
    /// game's own, and ends as unrepresentable in <see cref="Pull"/> if the event is not built),
    /// else when some event of the list is allowed, unmet and built. A director answers
    /// <see cref="IMapDirector.CanHold"/> for <see cref="RolledRoom.Event"/> with this.
    /// </summary>
    public static bool CanHold(Run run, Func<string, bool>? isBuilt = null) => IsStrict(run) || Peek(run, isBuilt).Found;

    /// <summary>
    /// Takes the event of an Event room that was just entered, as the game does when it makes
    /// the room: the walk moves the act's counter past everything it steps over, and the event
    /// is met from now on. Returns its class name. The room's own count
    /// (<see cref="MapState.MarkRoomVisited"/>) is the caller's, after the room is open.
    /// </summary>
    /// <exception cref="RunUnrepresentableException">The game's event is not built and the run is strict, or no built event is
    /// allowed and unmet (a director that asks <see cref="CanHold"/> first never gets there).</exception>
    public static string Pull(Run run, Func<string, bool>? isBuilt = null)
    {
        var state = run.State;
        var rooms = state.Part<RunWorld>().Rooms[state.ActIndex];
        var pull = Walk(run, rooms, isBuilt ?? IsBuilt, peek: false);
        // The log is made the first time something is passed over. In strict mode its first entry throws.
        foreach (var passed in pull.PassedOver) state.Part<LeftOutLog>().PassedOver(state, "event", passed, "the event of an Event room");
        if (pull.Event is not { } taken) throw new RunUnrepresentableException(pull.Reason!);
        state.Part<EventsState>().MarkVisited(taken);
        return taken;
    }

    private static bool IsStrict(Run run) => run.State.FindPart<LeftOutLog>()?.Strict ?? false;

    private static EventPull Walk(Run run, ActRooms rooms, Func<string, bool> isBuilt, bool peek)
    {
        var count = rooms.Events.Count;
        if (count == 0) throw new InvalidOperationException($"The act {rooms.Act} has no events to take one from.");

        var state = run.State;
        var floor = FloorsBehind(run);
        var met = state.FindPart<EventsState>();
        bool Valid(string name) => IsAllowed(run, name, floor, peek) && !(met?.HasVisited(name) ?? false);

        // The game's own walk: at most one lap, and after a lap without a find the counter has
        // gone all the way round and stands on the entry it started on.
        var steppedOver = new List<string>();
        var steps = 0;
        var found = false;
        for (; steps < count; steps++)
        {
            if (Valid(rooms.NextEvent))
            {
                found = true;
                break;
            }
            steppedOver.Add(rooms.NextEvent);
            rooms.EventsVisited++;
        }
        var game = rooms.NextEvent;
        if (isBuilt(game)) return new EventPull(game, game, rooms.EventsVisited, steppedOver, [], !found);

        var floorOfRoom = floor + 1;
        var passedOver = new List<string> { game };
        if (IsStrict(run))
            return new EventPull(null, game, rooms.EventsVisited, steppedOver, passedOver, !found,
                string.Create(CultureInfo.InvariantCulture, $"the Event room on floor {floorOfRoom} holds {game}, which the arena leaves out ({WhyNotBuilt(game)})"));

        // The house rule: on along the rest of the lap for an entry that is allowed, unmet and built.
        if (found)
        {
            for (steps++; steps < count; steps++)
            {
                rooms.EventsVisited++;
                var next = rooms.NextEvent;
                if (!Valid(next)) continue;
                if (isBuilt(next)) return new EventPull(next, game, rooms.EventsVisited, steppedOver, passedOver, false);
                passedOver.Add(next);
            }
        }
        return new EventPull(null, game, rooms.EventsVisited, steppedOver, passedOver, !found,
            string.Create(CultureInfo.InvariantCulture, $"no event the arena holds is allowed and unmet for the Event room on floor {floorOfRoom} (the game takes {game})"));
    }

    /// <summary>Why the arena does not hold an event: the reason it was left out with, or that it is not built yet.</summary>
    public static string WhyNotBuilt(string eventClassName) =>
        Content.Registry.IsExcluded(ContentKind.Event, eventClassName, out var reason) ? reason : "not built yet";
}
