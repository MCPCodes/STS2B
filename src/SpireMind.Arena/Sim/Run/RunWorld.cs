using System.Globalization;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// The part of a run's state that the seed makes and the random numbers come from: the
/// streams, the acts, each act's rooms, both relic bags and the odds of an Unknown room. It
/// lives in the <see cref="RunState"/> as a <see cref="RunPart"/> and is copied with the run:
/// <code>
/// var world = RunWorld.Begin(run.State);                       // once, before the first room
/// var world = run.State.Part&lt;RunWorld&gt;();                      // everywhere else
/// var rng = world.Streams.Draw(RunStream.Rewards, Purpose.Gold);
/// </code>
/// The run's two other odds, the card rarity offset and the potion chance, are numbers of
/// <see cref="RunState"/> itself; <see cref="RollCardRarity"/> and <see cref="RollPotionDrop"/>
/// roll on them.
/// </summary>
public sealed class RunWorld : RunPart
{
    private RunStreams? _streams;
    private RunUnlocks _unlocks = RunUnlocks.All;
    private List<string> _acts = [];
    private List<ActRooms> _rooms = [];
    private RelicBag _sharedBag = new(refreshAllowed: true);
    private RelicBag _playerBag = new();

    /// <summary>False for the empty part a run has before <see cref="Begin(RunState, RunUnlocks?, StreamMode, string, IReadOnlyList{string}?)"/> was called.</summary>
    public bool IsBegun => _streams is not null;

    /// <summary>Every random number of the run: see <see cref="RunStreams.Draw"/>.</summary>
    /// <exception cref="InvalidOperationException">The world was not begun.</exception>
    public RunStreams Streams => _streams ?? throw new InvalidOperationException("The run's world was not begun: call RunWorld.Begin(state) before the first room.");

    public RunUnlocks Unlocks => _unlocks;

    /// <summary>The character's class name ("Ironclad"): whose relics the player's bag holds.</summary>
    public string Character { get; private set; } = "Ironclad";

    /// <summary>The run's acts by class name, first act first. Only Overgrowth is built as a first act:
    /// a run whose first act is Underdocks is the game's Underdocks run for that seed.</summary>
    public IReadOnlyList<string> Acts => _acts;

    /// <summary>Each act's rooms, in act order: <c>Rooms[state.ActIndex]</c> is the current act's.</summary>
    public IReadOnlyList<ActRooms> Rooms => _rooms;

    public RelicBag SharedBag => _sharedBag;
    public RelicBag PlayerBag => _playerBag;

    /// <summary>What the next Unknown room becomes; back to its start values with every new act (<see cref="UnknownRoomOdds.ResetToBase"/>).</summary>
    public UnknownRoomOdds UnknownOdds { get; private set; } = new();

    /// <summary>How many numbers the start of the run took from up_front.</summary>
    public int UpFrontDraws { get; private set; }

    /// <summary>
    /// Makes the world of a run from its seed (<see cref="RunState.Seed"/>) and puts it into the
    /// state: the act roll, both bags, every act's rooms, the streams ready for the first room.
    /// </summary>
    /// <param name="unlocks">The profile; everything unlocked when left out.</param>
    /// <param name="acts">The acts, when they are not to be rolled (a check, or a save's own acts).</param>
    public static RunWorld Begin(RunState state, RunUnlocks? unlocks = null, StreamMode mode = StreamMode.Game, string character = "Ironclad", IReadOnlyList<string>? acts = null)
    {
        var world = new RunWorld();
        world.Begin(state.Seed, unlocks, mode, character, acts);
        state.SetPart(world);
        return world;
    }

    /// <summary>The same for a world that is not in a run's state (yet).</summary>
    /// <exception cref="InvalidOperationException">The world was begun before.</exception>
    public void Begin(string seed, RunUnlocks? unlocks = null, StreamMode mode = StreamMode.Game, string character = "Ironclad", IReadOnlyList<string>? acts = null)
    {
        if (IsBegun) throw new InvalidOperationException("The run's world was already begun.");
        var streams = new RunStreams(seed, mode);
        _unlocks = unlocks ?? RunUnlocks.All;
        var start = RunGeneration.Generate(streams, acts ?? RunGeneration.RollActs(streams.Seed, _unlocks), _unlocks, character);
        _streams = streams;
        Character = character;
        _acts = start.Acts.ToList();
        _rooms = start.Rooms.ToList();
        _sharedBag = start.SharedBag;
        _playerBag = start.PlayerBag;
        UpFrontDraws = start.UpFrontDraws;
    }

    // ---------- the two odds that are numbers of the run's state ----------

    /// <summary>A card rarity roll that moves the run's pity offset (<see cref="CardRarityOdds.Roll"/> on <see cref="RunState.CardRarityOffset"/>).</summary>
    public static CardRarity RollCardRarity(RunState state, GameRng rng, CardOddsType type)
    {
        var odds = new CardRarityOdds(state.CardRarityOffset);
        var rarity = odds.Roll(rng, type);
        state.CardRarityOffset = odds.Offset;
        return rarity;
    }

    /// <summary>Whether a won fight drops a potion; moves the run's potion chance (<see cref="PotionOdds.Roll"/> on <see cref="RunState.PotionChance"/>).</summary>
    public static bool RollPotionDrop(RunState state, GameRng rng, bool elite, bool forced = false)
    {
        var odds = new PotionOdds(state.PotionChance);
        var dropped = odds.Roll(rng, elite, forced);
        state.PotionChance = odds.Chance;
        return dropped;
    }

    // ---------- copies and dumps ----------

    public override RunPart Clone(CloneMap map) => new RunWorld
    {
        _streams = _streams?.Clone(),
        _unlocks = _unlocks,
        Character = Character,
        _acts = _acts.ToList(),
        _rooms = _rooms.Select(r => r.Clone()).ToList(),
        _sharedBag = _sharedBag.Clone(),
        _playerBag = _playerBag.Clone(),
        UnknownOdds = UnknownOdds.Clone(),
        UpFrontDraws = UpFrontDraws,
    };

    public override JsonNode? Dump()
    {
        static string Exact(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        static JsonArray Names(IEnumerable<string> names) => new(names.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
        static JsonObject Bag(RelicBag bag)
        {
            var lists = new JsonObject();
            foreach (var rarity in bag.Rarities) lists[rarity.ToString()] = Names(bag.Relics(rarity));
            return lists;
        }

        var rooms = new JsonArray();
        foreach (var act in _rooms)
        {
            rooms.Add(new JsonObject
            {
                ["act"] = act.Act,
                ["events"] = Names(act.Events),
                ["events_visited"] = act.EventsVisited,
                ["normal"] = Names(act.NormalEncounters),
                ["normal_visited"] = act.NormalEncountersVisited,
                ["elites"] = Names(act.EliteEncounters),
                ["elites_visited"] = act.EliteEncountersVisited,
                ["boss"] = act.Boss,
                ["boss_visited"] = act.BossEncountersVisited,
                ["ancient"] = act.Ancient,
            });
        }
        return new JsonObject
        {
            ["streams"] = _streams?.Dump(),
            ["character"] = Character,
            ["acts"] = Names(_acts),
            ["rooms"] = rooms,
            ["shared_bag"] = Bag(_sharedBag),
            ["player_bag"] = Bag(_playerBag),
            ["unknown_odds"] = new JsonObject
            {
                ["monster"] = Exact(UnknownOdds.Monster), ["elite"] = Exact(UnknownOdds.Elite),
                ["treasure"] = Exact(UnknownOdds.Treasure), ["shop"] = Exact(UnknownOdds.Shop),
            },
        };
    }
}
