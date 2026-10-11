using System.Globalization;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// The fifteen long streams of a run, by the game's names: twelve of the run and, from
/// <see cref="Rewards"/> on, three of the player. Each runs for the whole run: the shuffle of
/// fight 5 goes on where fight 4 stopped.
/// </summary>
public enum RunStream
{
    /// <summary>Everything made when the run starts: both relic bags, the Ancients, every act's lists.</summary>
    UpFront,
    Shuffle,
    /// <summary>What an Unknown room turns out to be.</summary>
    UnknownMapPoint,
    CombatCardGeneration,
    CombatPotionGeneration,
    CombatCardSelection,
    CombatEnergyCosts,
    CombatTargets,
    MonsterAi,
    /// <summary>Monster HP rolls, and the one-off rolls of relics outside fights.</summary>
    Niche,
    CombatOrbs,
    /// <summary>The rarity of a treasure room's relic.</summary>
    TreasureRoomRelics,

    /// <summary>Player: card rewards, the potion chance, which potion, relic rarities, chest gold, fight gold.</summary>
    Rewards,
    /// <summary>Player: which card and potion a shop holds, and every price.</summary>
    Shops,
    /// <summary>Player: what a transformed card becomes.</summary>
    Transformations,
}

/// <summary>How a run hands out its random numbers (plan 5.4).</summary>
public enum StreamMode
{
    /// <summary>The game's own fifteen long streams: a seed gives the run the game gives for it and
    /// the same actions. For the golden saves and for measuring what share of real runs is built.</summary>
    Game,

    /// <summary>A house rule for comparing players: every roll has a stream of its own, named by
    /// the place and by what the roll decides, so two players on the same node meet the same
    /// numbers whatever they did before. What the seed alone decides (lists, bags, map, each
    /// encounter's line-up, each event's rolls) is the same in both modes.</summary>
    Keyed,
}

/// <summary>
/// What a roll decides. Every draw outside the run's start names one, so that keyed mode can
/// give it a stream of its own; game mode ignores it. <paramref name="A"/> and
/// <paramref name="B"/> tell apart several rolls of one kind at one place: which reward row,
/// which card of it, which shop slot.
/// </summary>
public readonly record struct Purpose(string Key, int A = 0, int B = 0)
{
    /// <summary>What an Unknown room becomes.</summary>
    public static readonly Purpose UnknownRoom = new("unknown_room");

    /// <summary>Whether a won fight drops a potion.</summary>
    public static readonly Purpose PotionChance = new("potion_chance");

    /// <summary>The gold of a won fight.</summary>
    public static readonly Purpose Gold = new("gold");

    /// <summary>Which potion a potion reward is (rarity and pick): <paramref name="row"/> counts the potion rewards of the room.</summary>
    public static Purpose Potion(int row = 0) => new("potion", row);

    /// <summary>One card of a card reward (rarity, pick and upgrade roll): which card reward of the room, which card of it.</summary>
    public static Purpose Card(int reward, int card) => new("card", reward, card);

    /// <summary>The rarity of a relic reward: <paramref name="row"/> counts the relic rewards of the room.</summary>
    public static Purpose RelicRarity(int row = 0) => new("relic_rarity", row);

    /// <summary>The gold a chest pays when it is opened.</summary>
    public static readonly Purpose ChestGold = new("chest_gold");

    /// <summary>The rarity of a chest's relic.</summary>
    public static readonly Purpose ChestRarity = new("chest_rarity");

    /// <summary>Which shop card is on sale.</summary>
    public static readonly Purpose ShopSale = new("shop_sale");

    public static Purpose ShopCard(int slot) => new("shop_card", slot);
    public static Purpose ShopPrice(int slot) => new("shop_price", slot);
    public static Purpose ShopPotion(int slot) => new("shop_potion", slot);
    public static Purpose ShopRelicRarity(int slot) => new("shop_relic_rarity", slot);

    /// <summary>What a transformed card becomes: <paramref name="card"/> counts the cards of one transform.</summary>
    public static Purpose Transform(int card = 0) => new("transform", card);

    /// <summary>A relic's own roll (Fishing Rod, War Paint, Scroll Boxes), by the relic's class name.</summary>
    public static Purpose Relic(string className, int index = 0) => new("relic:" + className, index);

    /// <summary>A stream of a fight, by the game's stream name; it lasts for that fight.</summary>
    public static Purpose Fight(RunStream stream) => new("fight:" + RunStreams.NameOf(stream));

    /// <summary>Any other purpose: a short fixed name, never one that depends on what the player did before.</summary>
    public static Purpose Of(string key, int a = 0, int b = 0) => new(key, a, b);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Key}/{A}/{B}");
}

/// <summary>Where the run stands, for keyed streams: the act (0 for the first), the map node, and how
/// many rooms are stacked on that node (0 for the node's own room, 1 for a fight an event started).</summary>
public readonly record struct RunPlace(int Act, int Row, int Col, int Depth = 0)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Act}/{Row},{Col}/{Depth}");
}

/// <summary>
/// Every random number of a run comes from here. One call for every roll:
/// <c>var rng = streams.Draw(RunStream.Rewards, Purpose.PotionChance);</c> names the game's
/// stream and what the roll decides, and gives the stream to draw from. In
/// <see cref="StreamMode.Game"/> that is the game's long stream, seeded and consumed number
/// for number as in the game (a save's counters can be compared with <see cref="Counter"/>).
/// In <see cref="StreamMode.Keyed"/> it is a fresh stream of the place and the purpose.
/// <para>
/// Rules for callers. Draw every number a decision needs from the stream one call returned,
/// redraws included (a roll that landed on something left out is drawn again from the same
/// stream). Ask again for the next decision; never keep a stream across decisions or in a
/// field. The run's state holds one <see cref="RunStreams"/> and copies it with
/// <see cref="Clone"/>.
/// </para>
/// </summary>
public sealed class RunStreams
{
    /// <summary>The player streams are seeded with the seed plus the player's slot; single player is slot 0.</summary>
    public const int PlayerSlot = 0;

    private static readonly RunStream[] All = Enum.GetValues<RunStream>();

    private readonly GameRng[] _long;
    private Dictionary<string, int> _asked = new(StringComparer.Ordinal);

    public RunSeed Seed { get; }
    public StreamMode Mode { get; }

    /// <summary>Where the run stands. Only keyed mode reads it; set it in both modes.</summary>
    public RunPlace Place { get; private set; }

    public RunStreams(RunSeed seed, StreamMode mode = StreamMode.Game)
    {
        Seed = seed;
        Mode = mode;
        _long = new GameRng[All.Length];
        foreach (var stream in All) _long[(int)stream] = new GameRng(LongSeed(seed, stream));
    }

    public RunStreams(string seed, StreamMode mode = StreamMode.Game) : this(new RunSeed(seed), mode) { }

    /// <summary>Everything that tells two stream sets apart, for comparing two runs: the seed and the mode,
    /// how many numbers each long stream has given, the place, and how often each keyed purpose was asked there.</summary>
    public JsonObject Dump()
    {
        var counters = new JsonObject();
        foreach (var stream in All) counters[NameOf(stream)] = Counter(stream);
        var asked = new JsonObject();
        foreach (var (key, times) in _asked.OrderBy(a => a.Key, StringComparer.Ordinal)) asked[key] = times;
        return new JsonObject { ["seed"] = Seed.Text, ["mode"] = Mode.ToString(), ["place"] = Place.ToString(), ["counters"] = counters, ["asked"] = asked };
    }

    private RunStreams(RunStreams other)
    {
        Seed = other.Seed;
        Mode = other.Mode;
        Place = other.Place;
        _long = other._long.Select(s => s.Clone()).ToArray();
        _asked = new Dictionary<string, int>(other._asked, StringComparer.Ordinal);
    }

    public RunStreams Clone() => new(this);

    // ---------- names and seeds, as the game makes them ----------

    /// <summary>The game's name of a stream: its enum member in snake case ("up_front", "monster_ai", "rewards").</summary>
    public static string NameOf(RunStream stream) => GameNames.SnakeCase(stream.ToString());

    public static bool IsPlayerStream(RunStream stream) => stream >= RunStream.Rewards;

    /// <summary>The seed number of a long stream: the run's seed number (plus the player's slot
    /// for a player stream) plus the hash of the stream's name, in 32 bits.</summary>
    public static uint LongSeed(RunSeed seed, RunStream stream) => unchecked(
        (IsPlayerStream(stream) ? seed.Hash + PlayerSlot : seed.Hash) + (uint)RngSet.NameHash(NameOf(stream)));

    /// <summary>The stream that rolls the acts of a run, before anything else exists: seed number plus the hash of "act_selection".</summary>
    public static GameRng ActSelection(RunSeed seed) => Named(seed, "act_selection");

    /// <summary>The stream the map of an act is generated from: "act_1_map" for the first act. A new one every time a map is made.</summary>
    public GameRng ActMap(int actNumber) => Named(Seed, string.Create(CultureInfo.InvariantCulture, $"act_{actNumber}_map"));

    /// <summary>The stream of one encounter's line-up: seed number plus the total floor plus the hash of
    /// the encounter's id ("NIBBITS_WEAK"). The same in both modes: the game ties it to the place already.</summary>
    public GameRng Encounter(string encounterClassName, int totalFloor) => new(unchecked(
        (uint)((int)Seed.Hash + totalFloor + RngSet.NameHash(GameNames.Slug(encounterClassName)))));

    /// <summary>The stream of one event (and of Neow): seed number plus the player's slot plus the hash of the
    /// event's id ("THIS_OR_THAT"). The same whenever the event is met, and in both modes.</summary>
    public GameRng Event(string eventClassName) => new(unchecked(
        (uint)((int)Seed.Hash + PlayerSlot + RngSet.NameHash(GameNames.Slug(eventClassName)))));

    private static GameRng Named(RunSeed seed, string name) => new(unchecked(seed.Hash + (uint)RngSet.NameHash(name)));

    // ---------- the one draw call ----------

    /// <summary>
    /// The stream for one roll. <paramref name="stream"/> is the stream the game draws this roll
    /// from; <paramref name="purpose"/> says what it decides. <see cref="RunStream.UpFront"/> is
    /// always the long stream: what it makes belongs to the seed in both modes.
    /// </summary>
    public GameRng Draw(RunStream stream, Purpose purpose)
    {
        if (Mode == StreamMode.Game || stream == RunStream.UpFront) return _long[(int)stream];
        return Keyed(purpose);
    }

    /// <summary>The game's long stream itself, in either mode: for reading it, comparing it with a save and fast-forwarding it.</summary>
    public GameRng Long(RunStream stream) => _long[(int)stream];

    /// <summary>How many numbers a long stream has given: what a save writes under <c>rng.counters</c>.</summary>
    public int Counter(RunStream stream) => _long[(int)stream].Counter;

    /// <summary>Moves a long stream on to a counter a save names, as the game does on loading.</summary>
    /// <exception cref="InvalidOperationException">The stream is already past it.</exception>
    public void FastForward(RunStream stream, int counter)
    {
        var rng = _long[(int)stream];
        if (rng.Counter > counter)
            throw new InvalidOperationException($"The stream {NameOf(stream)} has given {rng.Counter} numbers and cannot go back to {counter}.");
        while (rng.Counter < counter) rng.NextU64();
    }

    // ---------- the place, for keyed streams ----------

    /// <summary>The run entered a map node. Call it for every node, the start node of an act included (row 0).</summary>
    public void MoveTo(int act, int row, int col)
    {
        Place = new RunPlace(act, row, col);
        _asked.Clear();
    }

    /// <summary>A room was put on top of the node's room (an event started a fight).</summary>
    public void PushRoom() => Place = Place with { Depth = Place.Depth + 1 };

    /// <summary>The room on top is gone.</summary>
    public void PopRoom() => Place = Place with { Depth = Math.Max(0, Place.Depth - 1) };

    // One stream per place and purpose. The seed is a hash of all the parts written out as one
    // text: adding numbers up, as the game does for its own seeds, would make floor 7 with one
    // name equal floor 8 with a name whose hash is one less. A purpose asked for twice at one
    // place gets a second stream, so the two rolls differ.
    private GameRng Keyed(Purpose purpose)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"{Place}|{purpose}");
        _asked.TryGetValue(key, out var times);
        _asked[key] = times + 1;
        return new GameRng(KeyedSeed(Seed, Place, purpose, times));
    }

    /// <summary>The seed of a keyed stream: FNV-1a over "seed|act/row,col/depth|purpose/a/b|times".</summary>
    public static ulong KeyedSeed(RunSeed seed, RunPlace place, Purpose purpose, int times = 0)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{seed.Text}|{place}|{purpose}|{times}");
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var ch in text)
            {
                hash = (hash ^ (byte)ch) * 1099511628211UL;
                hash = (hash ^ (byte)(ch >> 8)) * 1099511628211UL;
            }
            return hash;
        }
    }

    // ---------- a fight ----------

    /// <summary>
    /// The streams of a fight that starts now, for <c>FightRequest.Rng</c>. In game mode they
    /// are copies of the run's long streams as they stand, so the fight goes on where the last
    /// one stopped; give them back with <see cref="TakeBack"/> when the fight is over
    /// (<c>run.LastFightRng</c>). In keyed mode they are fresh streams of the place, as a
    /// single arena fight has them. The encounter's own stream is the game's in both.
    /// </summary>
    public RngSet LendToFight(string encounterClassName, int totalFloor)
    {
        GameRng For(RunStream stream) => Mode == StreamMode.Game ? _long[(int)stream].Clone() : Keyed(Purpose.Fight(stream));
        return new RngSet(
            shuffle: For(RunStream.Shuffle),
            monsterAi: For(RunStream.MonsterAi),
            niche: For(RunStream.Niche),
            combatTargets: For(RunStream.CombatTargets),
            combatCardSelection: For(RunStream.CombatCardSelection),
            combatCardGeneration: For(RunStream.CombatCardGeneration),
            combatPotionGeneration: For(RunStream.CombatPotionGeneration),
            combatEnergyCosts: For(RunStream.CombatEnergyCosts),
            encounter: Encounter(encounterClassName, totalFloor));
    }

    /// <summary>The fight is over: in game mode the run's long streams go on from where the fight left its own. Nothing to do in keyed mode.</summary>
    public void TakeBack(RngSet fight)
    {
        if (Mode != StreamMode.Game) return;
        _long[(int)RunStream.Shuffle] = fight.Shuffle.Clone();
        _long[(int)RunStream.MonsterAi] = fight.MonsterAi.Clone();
        _long[(int)RunStream.Niche] = fight.Niche.Clone();
        _long[(int)RunStream.CombatTargets] = fight.CombatTargets.Clone();
        _long[(int)RunStream.CombatCardSelection] = fight.CombatCardSelection.Clone();
        _long[(int)RunStream.CombatCardGeneration] = fight.CombatCardGeneration.Clone();
        _long[(int)RunStream.CombatPotionGeneration] = fight.CombatPotionGeneration.Clone();
        _long[(int)RunStream.CombatEnergyCosts] = fight.CombatEnergyCosts.Clone();
    }
}
