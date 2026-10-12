using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>What the treasure rooms keep for the whole run: how many were entered. The game looks at that once, for the
/// first chest of a profile's very first run.</summary>
public sealed class TreasureState : RunPart
{
    /// <summary>Treasure rooms entered in this run, the one the player stands in included.</summary>
    public int RoomsEntered { get; internal set; }

    public override RunPart Clone(CloneMap map) => new TreasureState { RoomsEntered = RoomsEntered };

    public override JsonNode Dump() => new JsonObject { ["rooms_entered"] = RoomsEntered };
}

/// <summary>
/// The treasure room (the game's TreasureRoom, TreasureRoomRelicSynchronizer and the chest's
/// part of OneOffSynchronizer): entering it, and what its chest holds.
/// <code>
/// // from the director's EnterRoom, for arrival.Room == RoomKind.Treasure
/// TreasureRoom.Enter(run);
/// </code>
/// The flow is the game's:
/// <list type="bullet">
/// <item><b>Entering.</b> The listeners hear "after room entered". Then the chest's relic is
/// settled, long before the chest is opened: the listeners are asked whether there is treasure
/// at all (<see cref="RunHooks.ShouldGenerateTreasure"/>; Silver Crucible empties the first
/// chest); if so one number of the run's <c>treasure_room_relics</c> stream rolls the rarity
/// (<see cref="RunRelics.RollRarity"/>) and the relic is the front of that rarity's list in the
/// SHARED bag (<see cref="RunRelics.PullFromShared"/>). It has left the shared bag from then
/// on; it leaves the player's bag only when it is obtained.</item>
/// <item><b>Opening the chest.</b> The listeners are asked again; if there is treasure, one
/// number of the player's <c>rewards</c> stream gives 42 to 52 gold, paid at once through the
/// gold hooks (<see cref="Run.GainGold"/>). Then the room's rewards are offered, which are none
/// unless a listener adds some (<see cref="RunRewards.OfferForRoomEnd(Run, RewardRoom)"/>): their
/// screen, if there is one, comes before the relic. Then the relic is shown.</item>
/// <item><b>Taking the relic</b> obtains it (<see cref="Run.ObtainRelic(Relic, int)"/>: its pickup
/// effect runs) and turns the proceed button on. The game also lets a single player skip the
/// relic; the mod never offers that, and neither does the arena.</item>
/// </list>
/// <para>
/// A relic the arena has not built is handled by the pull's own rule (<see cref="RunRelics"/>):
/// in strict mode, or when no built relic is left to land on, entering the room ends the run
/// as unrepresentable; otherwise the next built relic of the list is taken and what was passed
/// over is noted in the run's <see cref="LeftOutLog"/>.
/// </para>
/// <para>
/// Left out by name: more than one player (one relic per player, the votes, the fight over a
/// relic, the consolation prize); the gold multiplier of an ascension level; the gold Spoils
/// Map adds to a chest it marked (that card rewrites the map of act 2); the game's fallback
/// relic Circlet for an empty bag (the run ends as unrepresentable instead).
/// </para>
/// </summary>
public static class TreasureRoom
{
    /// <summary>The least gold a chest pays (OneOffSynchronizer.DoTreasureRoomRewards).</summary>
    public const int MinGold = 42;

    /// <summary>The most gold a chest pays.</summary>
    public const int MaxGold = 52;

    /// <summary>The relic the game puts into the first chest of a profile's very first run, whatever the roll says
    /// (TreasureRoomRelicSynchronizer.TryGetRelicForTutorial).</summary>
    public const string FirstRunRelic = "Gorget";

    /// <summary>
    /// The run enters a treasure room (TreasureRoom.EnterInternal): the screen is up, the
    /// listeners hear "after room entered", and the chest's relic is rolled and pulled. The map
    /// point is entered already (<see cref="MapState.Enter"/>); a treasure room moves none of the
    /// act's counters.
    /// </summary>
    /// <param name="isBuilt">Which relics the arena holds; the registry when left out. For checks.</param>
    /// <param name="newRelic">Makes the relic of a class name; the registry when left out. For checks, together with
    /// <paramref name="isBuilt"/>. Both must answer the same way every time: the action that enters the room may run twice.</param>
    /// <exception cref="RunUnrepresentableException">The chest's relic is one the arena cannot give (see the class).</exception>
    public static TreasureScreen Enter(Run run, Func<string, bool>? isBuilt = null, Func<string, Relic>? newRelic = null)
    {
        if (run.Fight is not null) throw new InvalidOperationException("A treasure room cannot be entered while a fight is on.");
        var state = run.State;
        if (state.FindPart<RunWorld>() is not { IsBegun: true })
            throw new InvalidOperationException("A treasure room needs the run's world: its relic comes from the shared relic bag and its rolls from the run's streams (RunWorld.Begin).");

        state.Part<TreasureState>().RoomsEntered++;
        var screen = new TreasureScreen();
        run.Push(screen);
        run.AfterRoomEntered(RoomKind.Treasure);
        if (!run.IsOver) screen.BeginRelicPicking(run, isBuilt ?? RunRelics.IsBuilt, newRelic ?? Content.Registry.NewRelic);
        return screen;
    }
}

/// <summary>
/// The treasure room's screen, as the mod's TreasureReader shows the game's NTreasureRoom
/// (docs/screens.md, "Treasure"). The state's keys in order: <c>run</c>, <c>chest_open</c>,
/// <c>relics</c> (while the chest is open and its relic not taken: <c>n</c>, <c>name</c>,
/// <c>text</c>), <c>picked</c> and <c>relic_awarded</c> (once the relic is taken),
/// <c>player</c> (without potions), <c>potions</c>; never <c>boss</c>. The actions:
/// <c>open</c> while the chest is shut; <c>relic:&lt;n&gt;</c> while a relic is listed;
/// <c>proceed</c> once the chest is open and nothing is listed; then the potion ids.
/// <para>
/// <c>proceed</c> is never offered while an unpicked relic is listed: the mod does not let the
/// bot give a chest's relic up, although the game would. The relic is not shown before the
/// chest is open, though it is settled when the room is entered. A rewards screen the opening
/// brings (a relic that adds rewards to the room) lies over this screen until it is closed.
/// </para>
/// <para>
/// Not imitated, because the arena cannot be in those states: the settling times, a relic
/// still rising or still being awarded (the award is at once, so <c>relic_awarded</c> is true
/// whenever <c>picked</c> is there), a chest that does not react, co-op votes.
/// </para>
/// </summary>
public sealed class TreasureScreen : RunScreen
{
    private Relic? _relic;
    private bool _opened;
    private string? _picked;

    public override string Name => "treasure";

    /// <summary>The relic in the chest, until it is taken; null for an empty chest and after the pick.</summary>
    public Relic? Relic => _relic;

    /// <summary>The rarity the game's roll gave, the first number drawn for this chest; <see cref="RelicRarity.None"/> for a
    /// chest without treasure. The relic in the chest is of another rarity when the roll had to be made again
    /// because no built relic of this one was left.</summary>
    public RelicRarity RolledRarity { get; private set; }

    /// <summary>The class name of the relic the game itself puts into this chest, built or not; null for a chest without treasure.</summary>
    public string? GameRelic { get; private set; }

    /// <summary>The chest is open.</summary>
    public bool IsOpen => _opened;

    /// <summary>The gold the chest paid when it was opened, after the gold hooks; null while it is shut, 0 for a chest without treasure.</summary>
    public int? Gold { get; private set; }

    /// <summary>The title of the relic taken from the chest, or null.</summary>
    public string? Picked => _picked;

    // ---------- entering (TreasureRoomRelicSynchronizer.BeginRelicPicking) ----------

    internal void BeginRelicPicking(Run run, Func<string, bool> isBuilt, Func<string, Relic> newRelic)
    {
        if (!RunHooks.ShouldGenerateTreasure(run.Context)) return;

        var state = run.State;
        var world = state.Part<RunWorld>();
        // The rarity is rolled first, also when the first chest of a first run does not use it.
        var rng = world.Streams.Draw(RunStream.TreasureRoomRelics, Purpose.ChestRarity);
        RolledRarity = RunRelics.RollRarity(rng);

        string relic;
        if (world.Unlocks.NumberOfRuns == 0 && state.Part<TreasureState>().RoomsEntered == 1)
        {
            // The first chest a profile ever meets holds Gorget, and Gorget leaves the shared bag.
            world.SharedBag.Remove(TreasureRoom.FirstRunRelic);
            GameRelic = relic = TreasureRoom.FirstRunRelic;
            if (!isBuilt(relic))
                throw new RunUnrepresentableException(string.Create(CultureInfo.InvariantCulture,
                    $"the chest on floor {state.TotalFloor}: the first chest of a profile's first run holds the relic {relic}, which the arena does not hold ({RunRelics.WhyNotBuilt(relic)})"));
        }
        else
        {
            var strict = state.FindPart<LeftOutLog>()?.Strict ?? false;
            var pull = RunRelics.PullFromShared(state, RolledRarity, rng, strict, isBuilt);
            GameRelic = pull.GameRelic;
            if (!pull.Found)
                throw new RunUnrepresentableException(string.Create(CultureInfo.InvariantCulture, $"the chest on floor {state.TotalFloor}: {pull.Reason}"));
            // A rarity that was rolled again leaves no trace among the relics passed over: the game's own relic was the first.
            if (pull.Rerolls > 0 && pull.GameRelic is { } first && !pull.PassedOver.Contains(first))
                state.Part<LeftOutLog>().PassedOver(state, "relic", first, "a chest's relic");
            foreach (var passed in pull.PassedOver) state.Part<LeftOutLog>().PassedOver(state, "relic", passed, "a chest's relic");
            relic = pull.Relic!;
        }
        _relic = newRelic(relic);
    }

    // ---------- the snapshot (TreasureReader.Capture) ----------

    public override GameSnapshot Snapshot(Run run)
    {
        var state = new JsonObject
        {
            ["run"] = run.RunJson(),
            ["chest_open"] = _opened,
        };
        var actions = new List<LegalAction>();

        if (!_opened)
        {
            actions.Add(new LegalAction("open", "Open the chest"));
        }
        else if (_relic is { } relic)
        {
            // A single player's chest holds one relic. Nothing set up in a closed chest is shown.
            var entry = new JsonObject { ["n"] = 0 };
            foreach (var (key, value) in RunDescribe.Relic(relic)) entry[key] = value?.DeepClone();
            state["relics"] = new JsonArray(entry);
            actions.Add(new LegalAction("relic:0", $"Take relic {relic.Name}"));
        }
        else
        {
            actions.Add(new LegalAction("proceed", _picked is null ? "Leave the treasure room (no relic on offer)" : "Leave the treasure room"));
        }

        if (_picked is { } picked)
        {
            state["picked"] = picked;
            state["relic_awarded"] = true;
        }
        state["player"] = run.PlayerJson(PlayerKeys.NoPotions);
        state["potions"] = RunPotions.Describe(run);

        actions.AddRange(RunPotions.Legal(run));
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    // ---------- the actions (TreasureReader.Execute) ----------

    public override ActionResult Execute(Run run, string actionId)
    {
        if (RunPotions.TryExecute(run, actionId, out var potion)) return potion;
        if (actionId == "open") return Open(run);
        if (actionId == "proceed") return Proceed(run);
        if (actionId.StartsWith("relic:", StringComparison.Ordinal)
            && int.TryParse(actionId.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return Pick(run, n);
        return ActionResult.Fail($"Unknown treasure room action '{actionId}'.");
    }

    /// <summary>NTreasureRoom.OpenChest: the gold (OneOffSynchronizer.DoTreasureRoomRewards), the rewards a listener adds
    /// to the room (TreasureRoom.DoExtraRewardsIfNeeded), then the relic is on show.</summary>
    private ActionResult Open(Run run)
    {
        if (_opened) return ActionResult.Fail("The chest was already clicked once; waiting for it to open.");

        var gold = 0;
        if (RunHooks.ShouldGenerateTreasure(run.Context))
        {
            var rng = run.State.Part<RunWorld>().Streams.Draw(RunStream.Rewards, Purpose.ChestGold);
            gold = run.GainGold(rng.NextInt(TreasureRoom.MinGold, TreasureRoom.MaxGold + 1));
        }
        Gold = gold;
        _opened = true;
        RunRewards.OfferForRoomEnd(run, RewardRoom.Treasure);
        return ActionResult.Success;
    }

    /// <summary>TreasureRoomRelicSynchronizer.PickRelicLocally and, for a single player, the award that follows at once.</summary>
    private ActionResult Pick(Run run, int n)
    {
        if (_picked is { } already) return ActionResult.Fail($"Already picked {already}.");
        if (_relic is not { } relic) return ActionResult.Fail("The game isn't taking relic votes right now.");
        if (!_opened) return ActionResult.Fail("The relics are still appearing.");
        if (n != 0) return ActionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"There is no relic {n}."));

        _relic = null;
        _picked = relic.Name;
        run.ObtainRelic(relic);
        return ActionResult.Success;
    }

    private ActionResult Proceed(Run run)
    {
        // Never while the chest is shut or its relic not taken: leaving would lose the relic.
        if (!_opened || _relic is not null) return ActionResult.Fail("The relic hasn't been awarded yet; leaving now would lose it.");
        run.Pop();
        run.RoomFinished();
        return ActionResult.Success;
    }

    public override RunScreen Clone(CloneMap map) => new TreasureScreen
    {
        _relic = _relic is null ? null : CopyOf(_relic), _opened = _opened, _picked = _picked,
        RolledRarity = RolledRarity, GameRelic = GameRelic, Gold = Gold,
    };

    /// <summary>A relic in a chest is in nobody's list: a copy of the run copies it by itself.</summary>
    private static Relic CopyOf(Relic relic)
    {
        var map = new CloneMap();
        var copy = relic.CloneFor(map);
        map.Finish();
        return copy;
    }
}
